using System.Collections.Generic;
using System.Text.Json.Serialization;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(TemplateFilePayload))]
[JsonSerializable(typeof(List<TemplateDetail>))]
internal sealed partial class NetworkConfigJsonContext : JsonSerializerContext
{
}

internal sealed class TemplateFilePayload
{
    public List<TemplateFileEntryPayload> Templates { get; set; } = new();
}

internal sealed class TemplateFileEntryPayload
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TemplateFormData Data { get; set; } = new();
}
