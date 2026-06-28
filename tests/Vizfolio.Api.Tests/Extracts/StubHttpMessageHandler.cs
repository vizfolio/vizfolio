using System.Net;

namespace Vizfolio.Api.Tests.Extracts;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }

    public static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = Json(body) };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}
