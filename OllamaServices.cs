using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace networker
{
    public class OllamaService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public static async Task<List<string>> GetModelsAsync(string endpoint, string apiKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.TrimEnd('/')}/api/tags");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>();
            var models = new List<string>();
            if (result?.Models != null)
            {
                foreach (var model in result.Models) models.Add(model.Name);
            }
            return models;
        }

        public static async Task<string> ChatAsync(string endpoint, string apiKey, string model, string prompt)
        {
            var request = new OllamaRequest
            {
                Model = model,
                Messages = new List<OllamaMessage> { new OllamaMessage { Role = "user", Content = prompt } },
                Stream = false
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/api/chat")
            {
                Content = JsonContent.Create(request)
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var response = await _httpClient.SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Ollama returned {response.StatusCode}. Details: {errorBody}");
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            return result?.Message?.Content ?? "No response received.";
        }
    }

    public class OllamaTagsResponse
    {
        [JsonPropertyName("models")] public List<OllamaModelInfo> Models { get; set; }
    }

    public class OllamaModelInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; }
    }

    public class OllamaRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; }
        [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    public class OllamaMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; }
        [JsonPropertyName("content")] public string Content { get; set; }
    }

    public class OllamaResponse
    {
        [JsonPropertyName("message")] public OllamaMessage Message { get; set; }
    }
}