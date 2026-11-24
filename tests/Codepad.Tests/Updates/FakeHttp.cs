using System.Net;
using System.Text;

namespace Codepad.Tests.Updates;

/// <summary>An HTTP handler that answers from a table. A request that has no entry is a test failure.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> routes = new Dictionary<string, Func<HttpResponseMessage>>(StringComparer.Ordinal);

    public List<Uri> Requests { get; } = [];

    public Exception? Failure { get; set; }

    public void Add(string url, HttpStatusCode status, byte[] body, string? location = null, long? declaredLength = null) =>
        routes[url] = () =>
        {
            HttpResponseMessage response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (declaredLength is long length)
            {
                response.Content.Headers.ContentLength = length;
            }

            if (location is not null)
            {
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            }

            return response;
        };

    public void Add(string url, string body) => Add(url, HttpStatusCode.OK, Encoding.UTF8.GetBytes(body));

    public void AddStream(string url, Func<Stream> body, long? length = null) =>
        routes[url] = () =>
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) };
            if (length is long declared)
            {
                response.Content.Headers.ContentLength = declared;
            }

            return response;
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
        {
            throw new InvalidOperationException("The updater must not send credentials.");
        }

        if (Failure is not null)
        {
            return Task.FromException<HttpResponseMessage>(Failure);
        }

        return routes.TryGetValue(request.RequestUri!.ToString(), out Func<HttpResponseMessage>? make)
            ? Task.FromResult(make())
            : throw new InvalidOperationException("No route for " + request.RequestUri);
    }
}
