using System.Net;
using System.Text;

namespace CityFy.Retrieve.Spotify.Tests;

/// <summary>Returns queued responses in order and records the requests.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHttpHandler Respond(HttpStatusCode status, string json = "{}", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var resp = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            configure?.Invoke(resp);
            return resp;
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        return Task.FromResult(_responses.Dequeue()());
    }
}
