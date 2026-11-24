using System.Net;

namespace Codepad.Infrastructure.Updates;

/// <summary>An HTTP request of the updater broke a rule. The code is safe to log. The message never holds an address.</summary>
internal sealed class GuardedHttpException(string code, Exception? innerException = null) : Exception($"The update request failed: {code}.", innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Sends the requests of the updater. Each address is checked: HTTPS, an official host, the default port, no user name. The client
/// does not follow redirects by itself. This class follows a few, and checks each target with the same rules, so a redirect
/// cannot lead the app to another host. It never sends a token or a cookie.
/// </summary>
internal sealed class GuardedHttp(HttpClient client)
{
    internal const int MAX_REDIRECTS = 3;

    /// <summary>The longest time that a body can go without a byte. HttpClient.Timeout covers the headers only, so a stalled body needs its own limit.</summary>
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] s_hosts = ["api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    /// <summary>Makes an HTTP client that does not follow redirects, does not use cookies and has no default credentials.</summary>
    /// <param name="timeout">The timeout of one request.</param>
    /// <returns>The client.</returns>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        SocketsHttpHandler handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            MaxResponseHeadersLength = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        HttpClient created = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        return created;
    }

    /// <summary>Tells whether an address is acceptable for an update request.</summary>
    /// <param name="url">The address.</param>
    /// <returns>True for an official HTTPS address.</returns>
    public static bool IsAllowed(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && string.IsNullOrEmpty(url.UserInfo)
        && s_hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sends a GET request and follows a bounded number of redirects to official hosts.</summary>
    /// <param name="url">The first address.</param>
    /// <param name="accept">The value of the Accept header, or null.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The final response. The caller disposes it. The headers are read and the body is not.</returns>
    public async Task<HttpResponseMessage> GetAsync(Uri url, string? accept, CancellationToken cancellationToken)
    {
        Uri current = url;
        for (int hop = 0; hop <= MAX_REDIRECTS; hop++)
        {
            if (!IsAllowed(current))
            {
                throw new GuardedHttpException(hop == 0 ? "untrusted_url" : "untrusted_redirect");
            }

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("CodePad-Updater");
            if (accept is not null)
            {
                request.Headers.Accept.ParseAdd(accept);
            }

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GuardedHttpException("timeout");
            }
            catch (HttpRequestException exception)
            {
                throw new GuardedHttpException("network", exception);
            }

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw new GuardedHttpException("redirect_invalid");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                HttpStatusCode status = response.StatusCode;
                response.Dispose();
                throw new GuardedHttpException(status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ? "rate_limited" : $"http_{(int)status}");
            }

            return response;
        }

        throw new GuardedHttpException("too_many_redirects");
    }

    /// <summary>Reads one block and gives up when no byte arrives within <see cref="StallTimeout"/>.</summary>
    /// <param name="stream">The body.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <param name="limit">The stall limit. Null uses <see cref="StallTimeout"/>.</param>
    /// <returns>The number of bytes, or 0 at the end.</returns>
    public static async Task<int> ReadWithStallLimitAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken, TimeSpan? limit = null)
    {
        using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(limit ?? StallTimeout);
        try
        {
            return await stream.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GuardedHttpException("timeout");
        }
    }

    /// <summary>Reads a body into memory, up to a limit.</summary>
    /// <param name="response">The response.</param>
    /// <param name="maxBytes">The largest body.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes.</returns>
    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
        {
            throw new GuardedHttpException("too_large");
        }

        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await ReadWithStallLimitAsync(stream, chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new GuardedHttpException("too_large");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (HttpRequestException exception)
        {
            throw new GuardedHttpException("network", exception);
        }
        catch (IOException exception)
        {
            throw new GuardedHttpException("network", exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GuardedHttpException("timeout");
        }
    }
}
