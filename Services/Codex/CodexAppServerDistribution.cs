namespace networker.Services.Codex;

/// <summary>
/// Pinned official OpenAI codex-app-server package metadata for Windows x64.
/// Values must match scripts/Get-CodexAppServer.ps1 and release verification.
/// </summary>
public static class CodexAppServerDistribution
{
    public const string Version = "0.149.0";
    public const string ReleaseTag = "rust-v0.149.0";
    public const string Target = "x86_64-pc-windows-msvc";
    public const string Variant = "codex-app-server";
    public const int LayoutVersion = 1;

    public const string PackageAsset = "codex-app-server-package-x86_64-pc-windows-msvc.tar.gz";
    public const long PackageSizeBytes = 116_042_307;
    public const string PackageSha256 = "580207baa5ecabb8e42fd734bdb774ffcd82709ccd60bff8fa812b1b83962e28";

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
