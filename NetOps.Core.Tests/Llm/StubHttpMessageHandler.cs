using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetOps.Core.Tests.Llm;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }

    public static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new StubHttpMessageHandler(handler));
    }

    public static HttpResponseMessage Json(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    public static HttpResponseMessage NdJson(params string[] lines)
    {
        return new HttpResponseMessage
        {
            Content = new StringContent(string.Join("\n", lines) + "\n", System.Text.Encoding.UTF8, "application/x-ndjson"),
        };
    }

    public static HttpResponseMessage Sse(params string[] payloads)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var payload in payloads)
        {
            builder.Append("data: ").Append(payload).Append("\n\n");
        }

        builder.Append("data: [DONE]\n\n");
        return new HttpResponseMessage
        {
            Content = new StringContent(builder.ToString(), System.Text.Encoding.UTF8, "text/event-stream"),
        };
    }
}
