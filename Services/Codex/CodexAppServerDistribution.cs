namespace networker.Services.Codex;

/// <summary>
/// Pinned official OpenAI codex-app-server package metadata for Windows x64.
/// Values must match scripts/Get-CodexAppServer.ps1 and release verification.
/// </summary>
public static class CodexAppServerDistribution
{
    public const string Version = "0.147.0";
    public const string ReleaseTag = "rust-v0.147.0";
    public const string Target = "x86_64-pc-windows-msvc";
    public const string Variant = "codex-app-server";
    public const int LayoutVersion = 1;

    public const string PackageAsset = "codex-app-server-package-x86_64-pc-windows-msvc.tar.gz";
    public const long PackageSizeBytes = 110_054_928;
    public const string PackageSha256 = "c8908d687cf7caa3074921479726db32f96a295372c3544f1e96919a7254951f";

    public const string PackageRootRelative = "Codex";
    public const string EntrypointRelative = "bin/codex-app-server.exe";
    public const string ResourcesDirName = "codex-resources";
    public const string PathDirName = "codex-path";

    /// <summary>Relative paths that must exist after package extraction.</summary>
    public static readonly string[] RequiredRelativePaths =
    {
        "bin/codex-app-server.exe",
        "bin/codex-code-mode-host.exe",
        "codex-package.json",
        "codex-path/rg.exe",
        "codex-resources/codex-command-runner.exe",
        "codex-resources/codex-windows-sandbox-setup.exe",
    };
}
