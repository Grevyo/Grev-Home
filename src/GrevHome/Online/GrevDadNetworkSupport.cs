using System.Text.Json;

namespace GrevHome.Online;

/// <summary>
/// Shared plumbing for Grev Home's three Grev.dad network services (account linking, profile
/// sync, connection maintenance). Each service still owns its own endpoints, request/response
/// shapes and error handling; this class holds only the HttpClient construction, JSON response
/// reading and atomic local-file writing that were previously copy-pasted near-identically into
/// every one of them.
/// </summary>
internal static class GrevDadNetworkSupport
{
    /// <summary>
    /// Resolves the configured base URI (explicit override, then the GREV_DAD_BASE_URI environment
    /// variable, then <paramref name="defaultBaseUri"/>), validates it is absolute HTTPS, and builds
    /// an HttpClient against it with Grev Home's standard user agent.
    /// </summary>
    public static HttpClient CreateHttpClient(Uri? configuredBaseUri, Uri defaultBaseUri, TimeSpan timeout)
    {
        var configured = configuredBaseUri ?? TryReadConfiguredBaseUri() ?? defaultBaseUri;
        if (!configured.IsAbsoluteUri || configured.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Grev.dad base URI must be absolute HTTPS.", nameof(configuredBaseUri));
        }

        var client = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(configured),
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/Backbone-1");
        return client;
    }

    public static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions json,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(json, cancellationToken);
            return value ?? throw new InvalidDataException("Grev.dad returned an empty JSON response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Grev.dad returned an incompatible JSON response.", ex);
        }
    }

    /// <summary>Writes JSON to a temp file, fsyncs it, then atomically renames it over the target path.</summary>
    public static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        JsonSerializerOptions json,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
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

    public static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(value, UriKind.Absolute);
    }

    public static Uri? TryReadConfiguredBaseUri()
    {
        var value = Environment.GetEnvironmentVariable("GREV_DAD_BASE_URI");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }
}
