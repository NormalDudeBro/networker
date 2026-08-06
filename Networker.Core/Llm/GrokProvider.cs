using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Networker.Core.Llm;

public sealed class GrokProvider : ILlmProvider
{
    private const string BaseUrl = "https://api.x.ai/v1";
    private readonly LlmConfig _config;
    private readonly HttpClient _http;

    public GrokProvider(LlmConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
        _model = config.XaiModel ?? "grok-3";
    }

    public LlmProviderKind Kind => LlmProviderKind.Grok;
    public string Name => "Grok (xAI)";
    public bool SupportsStreaming => true;
    public bool SupportsTools => false;

    private string _model;
    public string Model
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value) ? "grok-3" : value;
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new
        {
            model = Model,
            messages = messages.Select(ToPayloadMessage).ToArray(),
            stream = false,
        };

        return SendChatAsync(body, cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new
        {
            model = Model,
            messages = messages.Select(ToPayloadMessage).ToArray(),
            stream = true,
        };

        using var request = BuildRequest(HttpMethod.Post, "/chat/completions", body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var payload in SseReader.ReadEvents(stream, cancellationToken).ConfigureAwait(false))
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/models");
        AddAuthHeader(request);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var models = new List<LlmModelInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(new LlmModelInfo { Id = id! });
                }
            }
        }

        return models;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.XaiApiKey))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/models");
            AddAuthHeader(request);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_config.XaiApiKey))
        {
            throw new LlmException(
                "Grok is not configured. Set the XAI_API_KEY environment variable (or .env) and restart the app.")
            {
                Provider = Name,
            };
        }
    }

    private async Task<LlmResponse> SendChatAsync(object body, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, "/chat/completions", body);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return new LlmResponse
            {
                Provider = Name,
                Model = Model,
                Content = content.GetString() ?? string.Empty,
            };
        }

        throw new LlmException("Grok returned an unexpected response shape.")
        {
            Provider = Name,
        };
    }

    private static object ToPayloadMessage(LlmMessage message) => new
    {
        role = message.Role,
        content = message.Content,
    };

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        AddAuthHeader(request);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.XaiApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.XaiApiKey);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
        throw new LlmException(
            $"Grok request failed: {(int)response.StatusCode} {response.ReasonPhrase} {detail}".TrimEnd())
        {
            Provider = "Grok (xAI)",
        };
    }

    private static async Task<string> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message))
                {
                    return "(" + message.GetString() + ")";
                }

                var raw = error.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return "(" + raw + ")";
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }
}

