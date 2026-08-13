using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Networker.Core.Llm;

public sealed class GeminiProvider : ILlmProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private readonly LlmConfig _config;
    private readonly HttpClient _http;

    public GeminiProvider(LlmConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
        _model = config.GeminiModel ?? "gemini-2.5-flash";
    }

    public LlmProviderKind Kind => LlmProviderKind.Gemini;
    public string Name => "Gemini";
    public LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models;
    public bool SupportsStreaming => true;
    public bool SupportsTools => false;

    private string _model;
    public string Model
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value) ? "gemini-2.5-flash" : value;
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = BuildRequestBody(messages);
        return SendGenerateAsync(body, streaming: false, cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = BuildRequestBody(messages);
        using var request = BuildRequest(HttpMethod.Post, $"/models/{Model}:streamGenerateContent?alt=sse", body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var payload in SseReader.ReadEvents(stream, cancellationToken).ConfigureAwait(false))
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                continue;
            }

            var parts = candidates[0]
                .TryGetProperty("content", out var content)
                    ? GetTextParts(content)
                    : Array.Empty<string>();

            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    yield return part;
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
        if (doc.RootElement.TryGetProperty("models", out var modelsElement))
        {
            foreach (var item in modelsElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var id = name!.StartsWith("models/", StringComparison.Ordinal) ? name[7..] : name;
                models.Add(new LlmModelInfo
                {
                    Id = id,
                    Name = item.TryGetProperty("displayName", out var displayProp) ? displayProp.GetString() : null,
                });
            }
        }

        return models;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.GeminiApiKey))
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
        if (string.IsNullOrWhiteSpace(_config.GeminiApiKey))
        {
            throw new LlmException(
                "Gemini is not configured. Set the GEMINI_API_KEY environment variable (or .env) and restart the app.")
            {
                Provider = Name,
            };
        }
    }

    private object BuildRequestBody(IReadOnlyList<LlmMessage> messages)
    {
        var systemText = string.Join("\n\n", messages
            .Where(m => m.Role == "system")
            .Select(m => m.Content));

        var contents = new List<object>();
        LlmMessage? pending = null;
        foreach (var message in messages.Where(m => m.Role != "system"))
        {
            if (pending is not null && pending.Role == message.Role)
            {
                pending = new LlmMessage(pending.Role, pending.Content + "\n\n" + message.Content);
                continue;
            }

            if (pending is not null)
            {
                contents.Add(ToContentPayload(pending));
            }

            pending = message;
        }

        if (pending is not null)
        {
            contents.Add(ToContentPayload(pending));
        }

        if (!string.IsNullOrWhiteSpace(systemText))
        {
            return new
            {
                contents = contents.ToArray(),
                systemInstruction = new
                {
                    parts = new[] { new { text = systemText } },
                },
            };
        }

        return new { contents = contents.ToArray() };
    }

    private static object ToContentPayload(LlmMessage message)
    {
        var role = message.Role switch
        {
            "assistant" => "model",
            _ => "user",
        };

        return new
        {
            role,
            parts = new[] { new { text = message.Content } },
        };
    }

    private async Task<LlmResponse> SendGenerateAsync(object body, bool streaming, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/models/{Model}:generateContent", body);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var parts = candidates[0].TryGetProperty("content", out var content)
                ? GetTextParts(content)
                : Array.Empty<string>();

            return new LlmResponse
            {
                Provider = Name,
                Model = Model,
                Content = string.Concat(parts),
            };
        }

        throw new LlmException("Gemini returned an unexpected response shape.")
        {
            Provider = Name,
        };
    }

    private static IEnumerable<string> GetTextParts(JsonElement content)
    {
        if (content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text))
                {
                    yield return text.GetString() ?? string.Empty;
                }
            }
        }
    }

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
        if (!string.IsNullOrWhiteSpace(_config.GeminiApiKey))
        {
            request.Headers.TryAddWithoutValidation("x-goog-api-key", _config.GeminiApiKey);
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
            $"Gemini request failed: {(int)response.StatusCode} {response.ReasonPhrase} {detail}".TrimEnd())
        {
            Provider = "Gemini",
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

