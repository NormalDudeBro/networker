using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Networker.Core.Llm;

public sealed class OllamaProvider : ILlmProvider
{
    private readonly LlmConfig _config;
    private readonly HttpClient _http;

    public OllamaProvider(LlmConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
        _model = config.OllamaModel ?? string.Empty;
    }

    private string Host => _config.OllamaHost.TrimEnd('/');

    public LlmProviderKind Kind => LlmProviderKind.Ollama;
    public string Name => "Ollama";
    public LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models;
    public bool SupportsStreaming => true;
    public bool SupportsTools => false;

    private string _model;
    public string Model
    {
        get => _model;
        set => _model = value ?? string.Empty;
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new LlmException("No Ollama model selected. Pick a model in Settings or the assistant panel.")
            {
                Provider = Name,
            };
        }

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
        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new LlmException("No Ollama model selected. Pick a model in Settings or the assistant panel.")
            {
                Provider = Name,
            };
        }

        var body = new
        {
            model = Model,
            messages = messages.Select(ToPayloadMessage).ToArray(),
            stream = true,
        };

        using var request = BuildRequest(HttpMethod.Post, "/api/chat", body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                yield break;
            }

            if (root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                var delta = content.GetString();
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return delta;
                }
            }
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Host + "/api/tags");
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
                var id = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
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
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Host + "/api/tags");
            AddAuthHeader(request);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<LlmResponse> SendChatAsync(object body, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, "/api/chat", body);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return new LlmResponse
            {
                Provider = Name,
                Model = Model,
                Content = content.GetString() ?? string.Empty,
            };
        }

        throw new LlmException("Ollama returned an unexpected response shape.")
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
        var request = new HttpRequestMessage(method, Host + path);
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
        if (!string.IsNullOrWhiteSpace(_config.OllamaApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OllamaApiKey);
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
            $"Ollama request failed: {(int)response.StatusCode} {response.ReasonPhrase} {detail}".TrimEnd())
        {
            Provider = "Ollama",
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
                var message = error.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return "(" + message + ")";
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }
}

