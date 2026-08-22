using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrevHome.Profiles;
using GrevHome.Storage;

namespace GrevHome.Online;

public sealed record GrevDadServerCapabilities(
    bool Linking,
    bool DeviceTokens,
    bool TokenRotation,
    bool PerDeviceRevocation,
    bool LinkMetadataSync,
    bool Friends,
    bool FriendRequests,
    bool Presence,
    bool Activity,
    bool SessionHistory,
    bool ProgressionSync,
    bool ContentIdentity,
    bool OfflineHistoryReplay,
    bool StalePresenceReplay);

public sealed record GrevDadServerLimits(
    int LinkRequestSeconds,
    int TokenLifetimeSeconds,
    int TokenRotationOverlapSeconds,
    int PresenceMinSeconds,
    int PresenceMaxSeconds,
    int SyncBatchSessions);

public sealed record GrevDadCapabilitiesSnapshot(
    int ApiVersion,
    bool Optional,
    string Environment,
    GrevDadServerCapabilities Capabilities,
    GrevDadServerLimits Limits,
    DateTimeOffset RetrievedAtUtc);

internal sealed record GrevDadCapabilitiesApiResponse(
    bool Ok,
    int ApiVersion,
    bool Optional,
    string Environment,
    GrevDadServerCapabilities Capabilities,
    GrevDadServerLimits Limits,
    string? Message);

internal sealed record GrevDadRotateTokenApiResponse(
    bool Ok,
    int ApiVersion,
    string? AccessToken,
    long? TokenExpiresAt,
    long? PreviousTokenValidUntil,
    string? Message);

/// <summary>
/// Maintains an optional Grev.dad device link after it already exists. This layer performs public
/// capability negotiation, local-link metadata reconciliation and safe credential rotation, but it
/// never participates in local Grev Home login or entitlement decisions. GrevDadAccountService
/// remains the account-state authority.
/// </summary>
public sealed class GrevDadConnectionMaintenanceService : IDisposable
{
    private const string AccessCredentialSlot = "access";
    private static readonly TimeSpan CapabilityCacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan RotationWindow = TimeSpan.FromDays(14);

    private readonly AppPaths _paths;
    private readonly GrevDadAccountService _accounts;
    private readonly WindowsCredentialSecretStore _secrets = new();
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GrevDadCapabilitiesSnapshot? _capabilities;
    private bool _disposed;

    public GrevDadConnectionMaintenanceService(
        AppPaths paths,
        GrevDadAccountService accounts,
        Uri? baseUri = null)
    {
        _paths = paths;
        _accounts = accounts;
        var configured = baseUri
            ?? TryReadConfiguredBaseUri()
            ?? accounts.BaseUri;
        if (!configured.IsAbsoluteUri || configured.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Grev.dad base URI must be absolute HTTPS.", nameof(baseUri));
        }

        _http = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(configured),
            Timeout = TimeSpan.FromSeconds(8)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/Backbone-1");
    }

    public GrevDadCapabilitiesSnapshot? LastCapabilities => _capabilities;

    public async Task<GrevDadCapabilitiesSnapshot> GetCapabilitiesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var cached = _capabilities;
        if (!forceRefresh && cached is not null &&
            DateTimeOffset.UtcNow - cached.RetrievedAtUtc < CapabilityCacheLifetime)
        {
            return cached;
        }

        using var response = await _http.GetAsync("api/grev-home/capabilities", cancellationToken);
        var payload = await ReadJsonAsync<GrevDadCapabilitiesApiResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || !payload.Ok)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(payload.Message)
                    ? $"Grev.dad capability negotiation failed with HTTP {(int)response.StatusCode}."
                    : payload.Message);
        }
        if (payload.ApiVersion != GrevDadAccountService.SupportedApiVersion)
        {
            throw new InvalidDataException(
                $"Grev.dad API {payload.ApiVersion} is not compatible with this Grev Home build (API {GrevDadAccountService.SupportedApiVersion}).");
        }
        if (!payload.Optional)
        {
            throw new InvalidDataException("Grev.dad reported an incompatible contract: the Grev Home integration must remain optional.");
        }

        var snapshot = new GrevDadCapabilitiesSnapshot(
            payload.ApiVersion,
            payload.Optional,
            payload.Environment,
            payload.Capabilities,
            payload.Limits,
            DateTimeOffset.UtcNow);
        _capabilities = snapshot;
        return snapshot;
    }

    public async Task<bool> MaintainLinkedProfileAsync(
        LocalProfile profile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var local = await _accounts.LoadLocalStateAsync(profile.GrevId, cancellationToken);
            if (local.State is not (GrevDadConnectionState.Linked or GrevDadConnectionState.Offline))
            {
                return false;
            }

            GrevDadCapabilitiesSnapshot capabilities;
            try
            {
                capabilities = await GetCapabilitiesAsync(false, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            {
                if (_capabilities is null)
                {
                    return false;
                }
                capabilities = _capabilities;
            }

            var validated = await _accounts.ValidateLinkedAccountAsync(profile.GrevId, cancellationToken);
            if (validated.State != GrevDadConnectionState.Linked)
            {
                return false;
            }

            if (validated.TokenExpiresAtUtc is { } expiresAt &&
                expiresAt - DateTimeOffset.UtcNow <= RotationWindow &&
                capabilities.Capabilities.TokenRotation)
            {
                await RotateCredentialAsync(profile.GrevId, cancellationToken);
            }

            if (capabilities.Capabilities.LinkMetadataSync)
            {
                await ReconcileLocalIdentityAsync(profile, cancellationToken);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReconcileLocalIdentityAsync(
        LocalProfile profile,
        CancellationToken cancellationToken)
    {
        var token = _secrets.Read(profile.GrevId, AccessCredentialSlot);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, "api/grev-home/link/metadata")
        {
            Content = JsonContent.Create(new
            {
                grevId = profile.GrevId,
                localUsername = profile.Username,
                localDisplayName = profile.DisplayName
            }, options: _json)
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _accounts.ValidateLinkedAccountAsync(profile.GrevId, cancellationToken);
            return;
        }

        var payload = await ReadJsonAsync<ApiEnvelope>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || !payload.Ok)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(payload.Message)
                    ? $"Grev.dad link metadata reconciliation failed with HTTP {(int)response.StatusCode}."
                    : payload.Message);
        }
        if (payload.ApiVersion is not null && payload.ApiVersion != GrevDadAccountService.SupportedApiVersion)
        {
            throw new InvalidDataException("Grev.dad returned an incompatible link metadata response.");
        }
    }

    private async Task RotateCredentialAsync(string grevId, CancellationToken cancellationToken)
    {
        var currentToken = _secrets.Read(grevId, AccessCredentialSlot);
        if (string.IsNullOrWhiteSpace(currentToken))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/grev-home/token/rotate");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", currentToken);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _accounts.ValidateLinkedAccountAsync(grevId, cancellationToken);
            return;
        }

        var payload = await ReadJsonAsync<GrevDadRotateTokenApiResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || !payload.Ok ||
            string.IsNullOrWhiteSpace(payload.AccessToken) || payload.TokenExpiresAt is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(payload.Message)
                    ? "Grev.dad did not return a complete rotated device credential."
                    : payload.Message);
        }
        if (payload.ApiVersion != GrevDadAccountService.SupportedApiVersion)
        {
            throw new InvalidDataException("Grev.dad returned a rotated credential for an incompatible API version.");
        }

        var newExpiry = DateTimeOffset.FromUnixTimeSeconds(payload.TokenExpiresAt.Value);
        _secrets.Write(grevId, AccessCredentialSlot, payload.AccessToken);
        await UpdateLocalTokenExpiryAsync(grevId, newExpiry, cancellationToken);
        await _accounts.LoadLocalStateAsync(grevId, cancellationToken);
    }

    private async Task UpdateLocalTokenExpiryAsync(
        string grevId,
        DateTimeOffset tokenExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var path = GetMetadataFile(grevId);
        if (!File.Exists(path))
        {
            throw new InvalidDataException("The Grev.dad device credential exists but its local link metadata is missing.");
        }

        GrevDadLinkMetadata metadata;
        try
        {
            await using var input = File.OpenRead(path);
            metadata = await JsonSerializer.DeserializeAsync<GrevDadLinkMetadata>(input, _json, cancellationToken)
                       ?? throw new InvalidDataException("The local Grev.dad link metadata is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The local Grev.dad link metadata is damaged.", ex);
        }

        var updated = metadata with
        {
            TokenExpiresAtUtc = tokenExpiresAtUtc,
            LastValidatedAtUtc = DateTimeOffset.UtcNow
        };
        await WriteJsonAtomicallyAsync(path, updated, cancellationToken);
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken)
                   ?? throw new InvalidDataException("Grev.dad returned an empty JSON response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Grev.dad returned an incompatible JSON response.", ex);
        }
    }

    private async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetMetadataFile(string grevId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "link.json");

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(value, UriKind.Absolute);
    }

    private static Uri? TryReadConfiguredBaseUri()
    {
        var value = Environment.GetEnvironmentVariable("GREV_DAD_BASE_URI");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        _gate.Dispose();
    }
}
