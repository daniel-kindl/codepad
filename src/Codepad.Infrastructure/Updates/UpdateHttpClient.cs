namespace Codepad.Infrastructure.Updates;

/// <summary>Makes the HTTP client of the updater: no automatic redirects, no cookies, no credentials. The updater checks each redirect itself.</summary>
public static class UpdateHttpClient
{
    /// <summary>Makes a client.</summary>
    /// <param name="timeout">The timeout of one request. A download needs a longer one than an API call.</param>
    /// <returns>The client. The caller keeps it for the life of the app.</returns>
    public static HttpClient Create(TimeSpan timeout) => GuardedHttp.CreateClient(timeout);
}
