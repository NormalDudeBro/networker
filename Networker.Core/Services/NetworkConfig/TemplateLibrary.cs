using System.Text.Json;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Library of predefined device templates, ported from NetworkConfigPro's
/// <c>src/gui/app.py</c> <c>TEMPLATES</c> dict. Built-in templates ship as an
/// embedded JSON resource; user templates persist to
/// <c>%LOCALAPPDATA%\Networker\custom_templates.json</c>.
/// </summary>
public sealed class TemplateLibrary : ITemplateLibrary
{
    private const string BuiltInResourceName = "Networker.Core.Resources.Templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _customPath;
    private readonly IReadOnlyList<TemplateDetail> _builtIn;
    private readonly Dictionary<string, TemplateDetail> _custom;
    private readonly object _sync = new();

    /// <summary>
    /// Initializes the template library.
    /// </summary>
    /// <param name="customTemplatesPath">
    /// Path for user templates. Defaults to
    /// <c>%LOCALAPPDATA%\Networker\custom_templates.json</c>. Tests may pass a
    /// temporary path.
    /// </param>
    public TemplateLibrary(string? customTemplatesPath = null)
    {
        _customPath = customTemplatesPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Networker",
            "custom_templates.json");
        _builtIn = LoadBuiltIn();
        _custom = LoadCustom();
    }

    /// <inheritdoc />
    public IReadOnlyList<TemplateInfo> GetTemplates()
    {
        lock (_sync)
        {
            return _builtIn
                .Select(detail => ToInfo(detail))
                .Concat(_custom.Values.Select(ToInfo))
                .ToList();
        }
    }

    /// <inheritdoc />
    public TemplateDetail? GetTemplate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_sync)
        {
            // User templates override built-ins with the same name.
            if (_custom.TryGetValue(name, out var custom))
            {
                return custom;
            }

            return _builtIn.FirstOrDefault(template => template.Name == name);
        }
    }

    /// <inheritdoc />
    public void SaveCustomTemplate(string name, TemplateDetail template)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(template);

        lock (_sync)
        {
            _custom[name] = template with { IsBuiltIn = false };
            SaveCustom();
        }
    }

    /// <inheritdoc />
    public bool DeleteCustomTemplate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_sync)
        {
            if (_custom.Remove(name))
            {
                SaveCustom();
                return true;
            }

            return false;
        }
    }

    private static TemplateInfo ToInfo(TemplateDetail detail) => new()
    {
        Name = detail.Name,
        Description = detail.Description,
        Vendor = detail.Vendor,
        IsBuiltIn = detail.IsBuiltIn,
    };

    private static IReadOnlyList<TemplateDetail> LoadBuiltIn()
    {
        var assembly = typeof(TemplateLibrary).Assembly;
        using var stream = assembly.GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidOperationException($"Embedded template resource '{BuiltInResourceName}' was not found.");

        using var reader = new StreamReader(stream);
        var file = JsonSerializer.Deserialize<TemplateFile>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidOperationException("Embedded template resource is not valid JSON.");

        return file.Templates
            .Select(entry => new TemplateDetail
            {
                Name = entry.Name,
                Description = entry.Description,
                Vendor = TemplateFormConverter.VendorFromDisplayName(entry.Data.Basic.Vendor),
                Config = TemplateFormConverter.Convert(entry.Data),
                FormData = entry.Data,
                IsBuiltIn = true,
            })
            .ToArray();
    }

    private Dictionary<string, TemplateDetail> LoadCustom()
    {
        if (!File.Exists(_customPath))
        {
            return new Dictionary<string, TemplateDetail>();
        }

        try
        {
            var json = File.ReadAllText(_customPath);
            var templates = JsonSerializer.Deserialize<List<TemplateDetail>>(json, JsonOptions)
                ?? new List<TemplateDetail>();

            // Last-wins on duplicate names (defensive against hand-edited files).
            var result = new Dictionary<string, TemplateDetail>();
            foreach (var template in templates)
            {
                result[template.Name] = template;
            }

            return result;
        }
        catch (JsonException)
        {
            // A corrupt custom-templates file is not worth crashing the app over;
            // start with an empty set. The file is rewritten on the next save.
            return new Dictionary<string, TemplateDetail>();
        }
    }

    private void SaveCustom()
    {
        var json = JsonSerializer.Serialize(_custom.Values.ToList(), JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(_customPath)!);
        File.WriteAllText(_customPath, json);
    }

    /// <summary>
    /// JSON container mirroring the embedded Templates.json root.
    /// </summary>
    private sealed class TemplateFile
    {
        public List<TemplateFileEntry> Templates { get; set; } = new();
    }

    /// <summary>
    /// One entry of the embedded Templates.json.
    /// </summary>
    private sealed class TemplateFileEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TemplateFormData Data { get; set; } = new();
    }
}
