using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrevHome.Profiles;
using GrevHome.Storage;

namespace GrevHome.Online;

/// <summary>
/// Owns every Grev Home ↔ Grev.dad network/account interaction. Local profile sign-in never
/// depends on this service: network failures become Offline snapshots and cached remote data.
/// Views and future social features consume this boundary rather than creating direct HTTP calls.
/// </summary>
public sealed class GrevDadAccountService : IDisposable
{
    public const int SupportedApiVersion = 1;

    private const string AccessCredentialSlot = "access";
    private const string PendingCredentialSlot = "pending";

    private readonly AppPaths _paths;
    private readonly WindowsCredentialSecretStore _secrets = new();
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Dictionary<string, GrevDadAccountSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public GrevDadAccountService(AppPaths paths, Uri? baseUri = null)
    {
        _paths = paths;
        var configured = baseUri
            ?? TryReadConfiguredBaseUri()
            ?? new Uri("https://grev.dad/", UriKind.Absolute);
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

    public event Action<string, GrevDadAccountSnapshot>? SnapshotChanged;

    public Uri BaseUri => _http.BaseAddress!;

    public GrevDadAccountSnapshot GetLastSnapshot(string grevId)
    {
        ValidateGrevId(grevId);
        lock (_snapshots)
        {
            return _snapshots.TryGetValue(grevId, out var snapshot)
                ? snapshot
                : GrevDadAccountSnapshot.Unlinked;
        }
    }

    public async Task<GrevDadAccountSnapshot> LoadLocalStateAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateGrevId(grevId);

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var metadata = await ReadMetadataAsync(grevId, cancellationToken);
            var accessToken = _secrets.Read(grevId, AccessCredentialSlot);
            var pendingSecret = _secrets.Read(grevId, PendingCredentialSlot);

            if (!string.IsNullOrWhiteSpace(accessToken) && metadata is not null)
            {
                var state = metadata.TokenExpiresAtUtc <= DateTimeOffset.UtcNow
                    ? GrevDadConnectionState.Expired
                    : GrevDadConnectionState.Linked;
                return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                    state,
                    metadata.Account,
                    state == GrevDadConnectionState.Expired ? "The Grev.dad device credential has expired." : null,
                    metadata.LastValidatedAtUtc,
                    metadata.TokenExpiresAtUtc));
            }

            if (!string.IsNullOrWhiteSpace(pendingSecret))
            {
                var pending = DeserializePendingCredential(pendingSecret);
                if (pending is not null && pending.ExpiresAtUtc > DateTimeOffset.UtcNow)
                {
                    return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                        GrevDadConnectionState.Linking,
                        metadata?.Account,
                        "Waiting for Grev.dad account approval.",
                        metadata?.LastValidatedAtUtc,
                        null));
                }

                TryDeleteSecret(grevId, PendingCredentialSlot);
            }

            return PublishSnapshot(grevId, GrevDadAccountSnapshot.Unlinked);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<GrevDadLinkStart> BeginLinkAsync(
        LocalProfile profile,
        string? deviceName = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);
        ValidateGrevId(profile.GrevId);

        var requestBody = new
        {
            grevId = profile.GrevId,
            username = profile.Username,
            displayName = profile.DisplayName,
            deviceName = string.IsNullOrWhiteSpace(deviceName)
                ? Environment.MachineName
                : deviceName.Trim()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/grev-home/link/start")
        {
            Content = JsonContent.Create(requestBody, options: _json)
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadJsonAsync<LinkStartApiResponse>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        EnsureApiVersion(payload.ApiVersion);

        if (string.IsNullOrWhiteSpace(payload.LinkId) ||
            string.IsNullOrWhiteSpace(payload.DeviceCode) ||
            string.IsNullOrWhiteSpace(payload.UserCode) ||
            !Uri.TryCreate(payload.VerificationUri, UriKind.Absolute, out var verificationUri))
        {
            throw new InvalidDataException("Grev.dad returned an incomplete link request.");
        }

        var expiresAt = FromUnixSeconds(payload.ExpiresAt);
        var pending = new GrevDadPendingCredential(
            payload.LinkId,
            payload.DeviceCode,
            expiresAt,
            Math.Clamp(payload.IntervalSeconds, 2, 30));
        _secrets.Write(profile.GrevId, PendingCredentialSlot, JsonSerializer.Serialize(pending, _json));

        PublishSnapshot(profile.GrevId, new GrevDadAccountSnapshot(
            GrevDadConnectionState.Linking,
            null,
            "Approve this GrevID from Grev.dad to finish linking.",
            DateTimeOffset.UtcNow,
            null));

        return new GrevDadLinkStart(
            payload.LinkId,
            payload.UserCode,
            verificationUri,
            expiresAt,
            pending.PollIntervalSeconds);
    }

    public async Task<GrevDadLinkPollResult> PollLinkAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateGrevId(grevId);
        var pendingSecret = _secrets.Read(grevId, PendingCredentialSlot);
        var pending = DeserializePendingCredential(pendingSecret);
        if (pending is null)
        {
            return new GrevDadLinkPollResult(
                GrevDadLinkPollState.Expired,
                null,
                "There is no active Grev.dad link request for this GrevID.");
        }

        if (pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            TryDeleteSecret(grevId, PendingCredentialSlot);
            PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Expired,
                null,
                "The Grev.dad link request expired.",
                null,
                null));
            return new GrevDadLinkPollResult(GrevDadLinkPollState.Expired, null, "The Grev.dad link request expired.");
        }

        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/grev-home/link/status?id={Uri.EscapeDataString(pending.LinkId)}",
            pending.DeviceCode);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadJsonAsync<LinkStatusApiResponse>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);

        var state = payload.Status.ToLowerInvariant() switch
        {
            "pending" => GrevDadLinkPollState.Pending,
            "approved" => GrevDadLinkPollState.Approved,
            "denied" => GrevDadLinkPollState.Denied,
            "expired" => GrevDadLinkPollState.Expired,
            "revoked" => GrevDadLinkPollState.Revoked,
            _ => throw new InvalidDataException("Grev.dad returned an unknown link state.")
        };

        if (state == GrevDadLinkPollState.Pending)
        {
            PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Linking,
                null,
                "Waiting for Grev.dad account approval.",
                DateTimeOffset.UtcNow,
                null));
            return new GrevDadLinkPollResult(state, null, payload.Message);
        }

        if (state != GrevDadLinkPollState.Approved)
        {
            TryDeleteSecret(grevId, PendingCredentialSlot);
            var connectionState = state switch
            {
                GrevDadLinkPollState.Denied => GrevDadConnectionState.Unlinked,
                GrevDadLinkPollState.Expired => GrevDadConnectionState.Expired,
                _ => GrevDadConnectionState.Revoked
            };
            PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                connectionState,
                null,
                payload.Message,
                DateTimeOffset.UtcNow,
                null));
            return new GrevDadLinkPollResult(state, null, payload.Message);
        }

        if (payload.ApiVersion is null)
        {
            throw new InvalidDataException("Grev.dad omitted its API version from the approved link response.");
        }
        EnsureApiVersion(payload.ApiVersion.Value);
        if (payload.Account is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            payload.TokenExpiresAt is null)
        {
            throw new InvalidDataException("Grev.dad approved the link but did not return complete device credentials.");
        }
        EnsureAccountMatchesGrevId(grevId, payload.Account);

        var tokenExpiresAt = FromUnixSeconds(payload.TokenExpiresAt.Value);
        _secrets.Write(grevId, AccessCredentialSlot, payload.AccessToken);
        TryDeleteSecret(grevId, PendingCredentialSlot);

        var metadata = new GrevDadLinkMetadata(
            payload.ApiVersion.Value,
            payload.Account,
            DateTimeOffset.UtcNow,
            tokenExpiresAt,
            DateTimeOffset.UtcNow);
        await WriteMetadataAsync(grevId, metadata, cancellationToken);
        await MergeCacheAsync(grevId, payload.Account, friends: null, activity: null, cancellationToken);

        PublishSnapshot(grevId, new GrevDadAccountSnapshot(
            GrevDadConnectionState.Linked,
            payload.Account,
            null,
            DateTimeOffset.UtcNow,
            tokenExpiresAt));
        return new GrevDadLinkPollResult(state, payload.Account, null);
    }

    public async Task<GrevDadAccountSnapshot> ValidateLinkedAccountAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateGrevId(grevId);
        var metadata = await ReadMetadataAsync(grevId, cancellationToken);
        var token = _secrets.Read(grevId, AccessCredentialSlot);
        if (metadata is null || string.IsNullOrWhiteSpace(token))
        {
            return PublishSnapshot(grevId, GrevDadAccountSnapshot.Unlinked);
        }

        if (metadata.ApiVersion != SupportedApiVersion)
        {
            return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Error,
                metadata.Account,
                $"This linked account uses Grev.dad API {metadata.ApiVersion}; Grev Home supports API {SupportedApiVersion}.",
                metadata.LastValidatedAtUtc,
                metadata.TokenExpiresAtUtc));
        }

        if (metadata.TokenExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Expired,
                metadata.Account,
                "The Grev.dad device credential has expired. Link the account again.",
                metadata.LastValidatedAtUtc,
                metadata.TokenExpiresAtUtc));
        }

        try
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/grev-home/me", token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                TryDeleteSecret(grevId, AccessCredentialSlot);
                return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                    GrevDadConnectionState.Revoked,
                    metadata.Account,
                    "The Grev.dad link was revoked or is no longer valid.",
                    DateTimeOffset.UtcNow,
                    metadata.TokenExpiresAtUtc));
            }

            var payload = await ReadJsonAsync<AccountApiResponse>(response, cancellationToken);
            EnsureSuccessful(response, payload.Ok, payload.Message);
            EnsureApiVersion(payload.ApiVersion);
            if (payload.Account is null)
            {
                throw new InvalidDataException("Grev.dad returned no account for this linked device.");
            }
            EnsureAccountMatchesGrevId(grevId, payload.Account);

            var updated = metadata with
            {
                Account = payload.Account,
                LastValidatedAtUtc = DateTimeOffset.UtcNow
            };
            await WriteMetadataAsync(grevId, updated, cancellationToken);
            await MergeCacheAsync(grevId, payload.Account, friends: null, activity: null, cancellationToken);
            return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Linked,
                payload.Account,
                null,
                DateTimeOffset.UtcNow,
                metadata.TokenExpiresAtUtc));
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            return PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Offline,
                metadata.Account,
                "Grev.dad is currently unavailable. Local Grev Home sign-in is unaffected.",
                metadata.LastValidatedAtUtc,
                metadata.TokenExpiresAtUtc));
        }
    }

    public async Task UnlinkAsync(
        string grevId,
        bool clearLocalIfOffline = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateGrevId(grevId);
        var token = _secrets.Read(grevId, AccessCredentialSlot);
        Exception? remoteFailure = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/grev-home/link/revoke", token);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    var payload = await ReadJsonAsync<ApiEnvelope>(response, cancellationToken);
                    EnsureSuccessful(response, payload.Ok, payload.Message);
                }
            }
            catch (Exception ex) when (IsNetworkFailure(ex))
            {
                remoteFailure = ex;
                if (!clearLocalIfOffline)
                {
                    throw;
                }
            }
        }

        TryDeleteSecret(grevId, AccessCredentialSlot);
        TryDeleteSecret(grevId, PendingCredentialSlot);
        TryDeleteFile(GetMetadataFile(grevId));
        PublishSnapshot(grevId, new GrevDadAccountSnapshot(
            GrevDadConnectionState.Unlinked,
            null,
            remoteFailure is null
                ? null
                : "Local link removed while Grev.dad was offline. The remote device token will expire or can be revoked from the site later.",
            null,
            null));
    }

    public async Task<IReadOnlyList<GrevDadFriend>> GetFriendsAsync(
        string grevId,
        bool allowCachedWhenOffline = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(grevId, HttpMethod.Get, "api/grev-home/friends", null, cancellationToken);
            var payload = await ReadJsonAsync<FriendsApiResponse>(response, cancellationToken);
            EnsureSuccessful(response, payload.Ok, payload.Message);
            var friends = (payload.Friends ?? Array.Empty<FriendApiPayload>())
                .Select(ToFriend)
                .ToArray();
            await MergeCacheAsync(grevId, account: null, friends, activity: null, cancellationToken);
            return friends;
        }
        catch (Exception ex) when (allowCachedWhenOffline && IsNetworkFailure(ex))
        {
            return (await ReadCacheAsync(grevId, cancellationToken)).Friends;
        }
    }

    public async Task<IReadOnlyList<GrevDadMemberSearchResult>> SearchMembersAsync(
        string grevId,
        string query,
        CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < 2)
        {
            return Array.Empty<GrevDadMemberSearchResult>();
        }

        using var response = await SendAuthorizedAsync(
            grevId,
            HttpMethod.Get,
            $"api/grev-home/users?q={Uri.EscapeDataString(query)}",
            null,
            cancellationToken);
        var payload = await ReadJsonAsync<MemberSearchApiResponse>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return payload.Users ?? Array.Empty<GrevDadMemberSearchResult>();
    }

    public async Task<GrevDadFriendRequestsSnapshot> GetFriendRequestsAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Get, "api/grev-home/friend-requests", null, cancellationToken);
        var payload = await ReadJsonAsync<FriendRequestsApiResponse>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return new GrevDadFriendRequestsSnapshot(
            (payload.Incoming ?? Array.Empty<FriendRequestApiPayload>()).Select(ToFriendRequest).ToArray(),
            (payload.Outgoing ?? Array.Empty<FriendRequestApiPayload>()).Select(ToFriendRequest).ToArray());
    }

    public async Task SendFriendRequestAsync(
        string grevId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(grevId, HttpMethod.Post, "api/grev-home/friend-requests", new { userId }, cancellationToken);
    }

    public async Task AcceptFriendRequestAsync(string grevId, string requestId, CancellationToken cancellationToken = default) =>
        await SendMutationAsync(grevId, HttpMethod.Post, $"api/grev-home/friend-requests/{Uri.EscapeDataString(requestId)}/accept", null, cancellationToken);

    public async Task DeclineFriendRequestAsync(string grevId, string requestId, CancellationToken cancellationToken = default) =>
        await SendMutationAsync(grevId, HttpMethod.Post, $"api/grev-home/friend-requests/{Uri.EscapeDataString(requestId)}/decline", null, cancellationToken);

    public async Task CancelFriendRequestAsync(string grevId, string requestId, CancellationToken cancellationToken = default) =>
        await SendMutationAsync(grevId, HttpMethod.Delete, $"api/grev-home/friend-requests/{Uri.EscapeDataString(requestId)}", null, cancellationToken);

    public async Task RemoveFriendAsync(string grevId, string userId, CancellationToken cancellationToken = default) =>
        await SendMutationAsync(grevId, HttpMethod.Delete, $"api/grev-home/friends/{Uri.EscapeDataString(userId)}", null, cancellationToken);

    public async Task<GrevDadPresence> UpdatePresenceAsync(
        string grevId,
        string availability,
        string activityType,
        string activityText,
        string statusText = "",
        int expiresInSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            availability,
            activityType,
            activityText,
            statusText,
            expiresInSeconds = Math.Clamp(expiresInSeconds, 60, 600)
        };
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Put, "api/grev-home/presence", body, cancellationToken);
        var payload = await ReadJsonAsync<PresenceApiResponse>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return ToPresence(payload.Presence);
    }

    public async Task<IReadOnlyList<GrevDadActivityEvent>> GetActivityAsync(
        string grevId,
        int limit = 50,
        bool allowCachedWhenOffline = true,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        try
        {
            using var response = await SendAuthorizedAsync(grevId, HttpMethod.Get, $"api/grev-home/activity?limit={limit}", null, cancellationToken);
            var payload = await ReadJsonAsync<ActivityApiResponse>(response, cancellationToken);
            EnsureSuccessful(response, payload.Ok, payload.Message);
            var events = (payload.Events ?? Array.Empty<ActivityApiPayload>()).Select(ToActivity).ToArray();
            await MergeCacheAsync(grevId, account: null, friends: null, events, cancellationToken);
            return events;
        }
        catch (Exception ex) when (allowCachedWhenOffline && IsNetworkFailure(ex))
        {
            return (await ReadCacheAsync(grevId, cancellationToken)).Activity;
        }
    }

    public async Task PublishAppActivityAsync(
        string grevId,
        bool started,
        string appId,
        string appName,
        string detail = "",
        string visibility = "friends",
        CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(
            grevId,
            HttpMethod.Post,
            "api/grev-home/activity",
            new
            {
                type = started ? "app.started" : "app.stopped",
                appId,
                appName,
                detail,
                visibility
            },
            cancellationToken);
    }

    public async Task CancelPendingLinkAsync(string grevId, CancellationToken cancellationToken = default)
    {
        ValidateGrevId(grevId);
        await Task.CompletedTask;
        TryDeleteSecret(grevId, PendingCredentialSlot);
        PublishSnapshot(grevId, GrevDadAccountSnapshot.Unlinked);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        string grevId,
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken)
    {
        ValidateGrevId(grevId);
        var token = _secrets.Read(grevId, AccessCredentialSlot);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("This GrevID is not linked to Grev.dad.");
        }

        var request = CreateAuthorizedRequest(method, relativeUri, token, body);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }
        request.Dispose();

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            TryDeleteSecret(grevId, AccessCredentialSlot);
            var metadata = await ReadMetadataAsync(grevId, cancellationToken);
            PublishSnapshot(grevId, new GrevDadAccountSnapshot(
                GrevDadConnectionState.Revoked,
                metadata?.Account,
                "The Grev.dad device link is no longer authorised.",
                DateTimeOffset.UtcNow,
                metadata?.TokenExpiresAtUtc));
            throw new InvalidOperationException("The Grev.dad device link is no longer authorised.");
        }

        return response;
    }

    private async Task SendMutationAsync(
        string grevId,
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(grevId, method, relativeUri, body, cancellationToken);
        var payload = await ReadJsonAsync<ApiEnvelope>(response, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string relativeUri,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _json);
        }
        return request;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken);
            return value ?? throw new InvalidDataException("Grev.dad returned an empty JSON response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Grev.dad returned an incompatible JSON response.", ex);
        }
    }

    private static void EnsureSuccessful(HttpResponseMessage response, bool ok, string? message)
    {
        if (response.IsSuccessStatusCode && ok)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(message)
                ? $"Grev.dad request failed with HTTP {(int)response.StatusCode}."
                : message);
    }

    private static bool IsNetworkFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException;

    private void EnsureApiVersion(int version)
    {
        if (version != SupportedApiVersion)
        {
            throw new InvalidDataException(
                $"Grev.dad API {version} is not compatible with this Grev Home build (API {SupportedApiVersion}).");
        }
    }

    private static void EnsureAccountMatchesGrevId(string grevId, GrevDadRemoteAccount account)
    {
        if (!string.Equals(grevId, account.GrevId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Grev.dad returned an account linked to a different GrevID.");
        }
    }

    private GrevDadAccountSnapshot PublishSnapshot(string grevId, GrevDadAccountSnapshot snapshot)
    {
        lock (_snapshots)
        {
            _snapshots[grevId] = snapshot;
        }
        SnapshotChanged?.Invoke(grevId, snapshot);
        return snapshot;
    }

    private async Task<GrevDadLinkMetadata?> ReadMetadataAsync(string grevId, CancellationToken cancellationToken)
    {
        var path = GetMetadataFile(grevId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<GrevDadLinkMetadata>(stream, _json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task WriteMetadataAsync(string grevId, GrevDadLinkMetadata metadata, CancellationToken cancellationToken)
    {
        var path = GetMetadataFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteJsonAtomicallyAsync(path, metadata, cancellationToken);
    }

    private async Task<GrevDadCachedData> ReadCacheAsync(string grevId, CancellationToken cancellationToken)
    {
        var path = GetCacheFile(grevId);
        if (!File.Exists(path))
        {
            return GrevDadCachedData.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<GrevDadCachedData>(stream, _json, cancellationToken)
                   ?? GrevDadCachedData.Empty;
        }
        catch (JsonException)
        {
            return GrevDadCachedData.Empty;
        }
        catch (IOException)
        {
            return GrevDadCachedData.Empty;
        }
    }

    private async Task MergeCacheAsync(
        string grevId,
        GrevDadRemoteAccount? account,
        IReadOnlyList<GrevDadFriend>? friends,
        IReadOnlyList<GrevDadActivityEvent>? activity,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCacheAsync(grevId, cancellationToken);
        var updated = new GrevDadCachedData(
            DateTimeOffset.UtcNow,
            account ?? existing.Account,
            friends ?? existing.Friends,
            activity ?? existing.Activity);
        var path = GetCacheFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteJsonAtomicallyAsync(path, updated, cancellationToken);
    }

    private async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
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
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string GetConnectionRoot(string grevId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad");

    private string GetMetadataFile(string grevId) =>
        Path.Combine(GetConnectionRoot(grevId), "link.json");

    private string GetCacheFile(string grevId) =>
        Path.Combine(GetConnectionRoot(grevId), "cache.json");

    private static GrevDadPendingCredential? DeserializePendingCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GrevDadPendingCredential>(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GrevDadFriend ToFriend(FriendApiPayload item) => new(
        item.UserId,
        item.Username,
        item.DisplayName,
        item.IsVerified,
        FromUnixSeconds(item.FriendsSince),
        ToPresence(item.Presence));

    private static GrevDadFriendRequest ToFriendRequest(FriendRequestApiPayload item) => new(
        item.Id,
        FromUnixSeconds(item.CreatedAt),
        item.User);

    private static GrevDadActivityEvent ToActivity(ActivityApiPayload item) => new(
        item.Id,
        new GrevDadFriendRequestUser(item.User.UserId, item.User.Username, item.User.DisplayName),
        item.Type,
        item.AppId,
        item.AppName,
        item.Detail,
        item.Visibility,
        FromUnixSeconds(item.OccurredAt));

    private static GrevDadPresence ToPresence(PresenceApiPayload? payload)
    {
        if (payload is null)
        {
            return new GrevDadPresence("offline", "", "none", "", null, null);
        }

        return new GrevDadPresence(
            payload.Availability,
            payload.StatusText,
            payload.ActivityType,
            payload.ActivityText,
            payload.ExpiresAt is null ? null : FromUnixSeconds(payload.ExpiresAt.Value),
            payload.UpdatedAt is null ? null : FromUnixSeconds(payload.UpdatedAt.Value));
    }

    private static DateTimeOffset FromUnixSeconds(long value) =>
        DateTimeOffset.FromUnixTimeSeconds(value);

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

    private static void ValidateGrevId(string grevId)
    {
        if (string.IsNullOrWhiteSpace(grevId) || grevId.Length > 58 || grevId[0] != 'G' ||
            grevId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Invalid GrevID.", nameof(grevId));
        }
    }

    private void TryDeleteSecret(string grevId, string slot)
    {
        try
        {
            _secrets.Delete(grevId, slot);
        }
        catch (Win32Exception)
        {
            // A stale credential is preferable to breaking local shell navigation. A later re-link
            // overwrites the slot and server-side revocation still protects the account.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _http.Dispose();
        _stateGate.Dispose();
    }
}
