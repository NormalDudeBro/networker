using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Validation issue severity levels.
/// </summary>
public enum ValidationSeverity
{
    Error = 0,
    Warning = 1,
    Info = 2,
}

/// <summary>
/// Validation issue categories.
/// </summary>
public enum ValidationCategory
{
    Security = 0,
    Syntax = 1,
    BestPractice = 2,
    Performance = 3,
    Redundancy = 4,
}

/// <summary>
/// Represents a validation issue found in configuration.
/// </summary>
public sealed record ValidationIssue
{
    public required ValidationSeverity Severity { get; init; }
    public required ValidationCategory Category { get; init; }
    public required string Message { get; init; }
    public required string Location { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

/// <summary>
/// Configuration validator service interface.
/// </summary>
public interface IConfigValidator
{
    /// <summary>
    /// Runs all validations on a device configuration.
    /// </summary>
    /// <param name="config">DeviceConfig to validate.</param>
    /// <returns>List of validation issues found.</returns>
    IReadOnlyList<ValidationIssue> Validate(NetworkDeviceConfig config);
}