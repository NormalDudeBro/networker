using System.Collections.Generic;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Template library service interface.
/// </summary>
public interface ITemplateLibrary
{
    /// <summary>
    /// Gets all available templates.
    /// </summary>
    IReadOnlyList<TemplateInfo> GetTemplates();

    /// <summary>
    /// Gets a specific template by name.
    /// </summary>
    TemplateDetail? GetTemplate(string name);

    /// <summary>
    /// Saves a custom template.
    /// </summary>
    void SaveCustomTemplate(string name, TemplateDetail template);

    /// <summary>
    /// Deletes a custom template.
    /// </summary>
    bool DeleteCustomTemplate(string name);
}

/// <summary>
/// Template information for listing.
/// </summary>
public sealed record TemplateInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Vendor Vendor { get; init; }
    public bool IsBuiltIn { get; init; }
}

/// <summary>
/// Detailed template data.
/// </summary>
public sealed record TemplateDetail
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Vendor Vendor { get; init; }
    public required NetworkDeviceConfig Config { get; init; }
    public bool IsBuiltIn { get; init; }
}