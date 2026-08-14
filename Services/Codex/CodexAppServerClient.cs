using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Codex;

namespace networker.Services.Codex;

public sealed class CodexAppServerClient : ICodexAppServerClient
{
    private const int MaximumProtocolLineCharacters = 4 * 1024 * 1024;
    private const int MaximumPendingRequests = 128;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _readerTask;
    private Task? _stderrTask;
    private long _requestId;
    private bool _disposed;

    public event Action<CodexNotification>? Notification;

    public bool IsRunning => _process is { HasExited: false };
    public string ComponentVersion => CodexAppServerDistribution.Version;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;

            string executable = ResolveExecutable();
            VerifyExecutable(executable);
            string codexHome = Path.Combine(AppSettings.GetLocalDataDirectory(), "Codex");
            Directory.CreateDirectory(codexHome);

            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = codexHome,
            };
            start.ArgumentList.Add("--listen");
            start.ArgumentList.Add("stdio://");
            start.ArgumentList.Add("--session-source");
            start.ArgumentList.Add("networker");
            start.ArgumentList.Add("--strict-config");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("cli_auth_credentials_store=\"keyring\"");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("features.remote_plugin=false");
            BuildEnvironment(start, codexHome);

            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.Exited += Process_Exited;
            if (!process.Start()) throw new InvalidOperationException("OpenAI Codex could not be started.");
            _process = process;
            _readerTask = ReadProtocolAsync(process, _lifetime.Token);
            _stderrTask = DrainDiagnosticsAsync(process, _lifetime.Token);

            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(StartupTimeout);
            string version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "development";
            await RequestAsync("initialize", new
            {
                clientInfo = new { name = "networker", title = "Networker", version },
                capabilities = new { experimentalApi = false, requestAttestation = false },
            }, startup.Token).ConfigureAwait(false);
            await SendNotificationAsync("initialized", null, startup.Token).ConfigureAwait(false);
        }
        catch
        {
            await StopProcessAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("A Codex method is required.", nameof(method));
        if (!IsRunning && method != "initialize") await StartAsync(cancellationToken).ConfigureAwait(false);
        if (_pending.Count >= MaximumPendingRequests) throw new CodexProtocolException("Codex is busy. Try again shortly.");

        long id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new CodexProtocolException("Unable to allocate a Codex request.");
        try
        {
            await SendAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(RequestTimeout);
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"Codex did not complete '{method}' in time.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsRunning) return;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopProcessAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task ReadProtocolAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;
                if (line.Length > MaximumProtocolLineCharacters) throw new CodexProtocolException("Codex returned an oversized protocol message.");
                using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out long id))
                {
                    if (_pending.TryGetValue(id, out TaskCompletionSource<JsonElement>? pending))
                    {
                        if (root.TryGetProperty("error", out JsonElement error))
                        {
                            int? code = error.TryGetProperty("code", out JsonElement codeValue) && codeValue.TryGetInt32(out int parsedCode) ? parsedCode : null;
                            string message = error.TryGetProperty("message", out JsonElement messageValue) ? messageValue.GetString() ?? "Codex request failed." : "Codex request failed.";
                            pending.TrySetException(new CodexProtocolException(Redact(message), code));
                        }
                        else if (root.TryGetProperty("result", out JsonElement result)) pending.TrySetResult(result.Clone());
                        else pending.TrySetException(new CodexProtocolException("Codex returned an invalid response."));
                    }
                    continue;
                }

                if (!root.TryGetProperty("method", out JsonElement methodElement)) continue;
                string method = methodElement.GetString() ?? string.Empty;
                JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement) ? paramsElement.Clone() : EmptyObject();
                if (root.TryGetProperty("id", out JsonElement serverRequestId))
                {
                    await RespondToServerRequestAsync(serverRequestId.Clone(), method, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    try { Notification?.Invoke(new CodexNotification(method, parameters)); }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailPending(new CodexProtocolException("The Codex protocol connection failed: " + Redact(ex.Message)));
        }
    }

    private async Task RespondToServerRequestAsync(JsonElement id, string method, CancellationToken cancellationToken)
    {
        object response = method switch
        {
            "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" => new { id = JsonId(id), result = new { decision = "cancel" } },
            _ => new { id = JsonId(id), error = new { code = -32601, message = "Networker does not support this Codex request." } },
        };
        await SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task DrainDiagnosticsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    Debug.WriteLine("Codex: " + Redact(line[..Math.Min(line.Length, 1024)]));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    private async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
        => await SendAsync(parameters is null ? new { method } : new { method, @params = parameters }, cancellationToken).ConfigureAwait(false);

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        Process process = _process is { HasExited: false } current ? current : throw new CodexProtocolException("Codex is not running.");
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        if (json.Length > MaximumProtocolLineCharacters) throw new CodexProtocolException("Codex request is too large.");
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    private void Process_Exited(object? sender, EventArgs e) => FailPending(new CodexProtocolException("Codex stopped unexpectedly."));

    private void FailPending(Exception exception)
    {
        foreach (TaskCompletionSource<JsonElement> completion in _pending.Values) completion.TrySetException(exception);
    }

    private async Task StopProcessAsync()
    {
        Process? process = _process;
        _process = null;
        if (process is null) return;
        try { process.StandardInput.Close(); } catch { }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
        process.Exited -= Process_Exited;
        process.Dispose();
    }

    private static void BuildEnvironment(ProcessStartInfo start, string codexHome)
    {
        string[] allowed =
        {
            "SystemRoot", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
            "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "COMSPEC",
            "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY",
        };
        var values = allowed.Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name))).Where(item => !string.IsNullOrEmpty(item.Value)).ToList();
        start.Environment.Clear();
        foreach (var item in values)
        {
            // Never forward provider secrets even if a proxy var name collides.
            if (LooksLikeSecret(item.Name) || LooksLikeSecret(item.Value!)) continue;
            start.Environment[item.Name] = item.Value!;
        }
        start.Environment["CODEX_HOME"] = codexHome;
        start.Environment["RUST_LOG"] = "error";
        // Explicitly ensure API-key auth cannot be injected via environment.
        start.Environment.Remove("OPENAI_API_KEY");
        start.Environment.Remove("CODEX_API_KEY");
        start.Environment.Remove("CHATGPT_API_KEY");
    }

    private static bool LooksLikeSecret(string value)
    {
        if (value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("REFRESH_TOKEN", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("SECRET", StringComparison.OrdinalIgnoreCase) && value.Contains('=')) return true;
        return false;
    }

    private static string ResolveExecutable()
    {
        foreach (string root in PackageRoots())
        {
            string executable = Path.Combine(root, "bin", "codex-app-server.exe");
            if (!File.Exists(executable)) continue;
            ValidatePackageLayout(root);
            return executable;
        }

        throw new FileNotFoundException("The OpenAI Codex component is missing. Repair or update Networker.");
    }

    private static IEnumerable<string> PackageRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, CodexAppServerDistribution.PackageRootRelative);
        yield return Path.Combine(Environment.CurrentDirectory, "artifacts", "codex", "win-x64");
        // Dev convenience: package extracted next to a lone Phase 0 binary is not accepted.
    }

    private static void ValidatePackageLayout(string packageRoot)
    {
        foreach (string relative in CodexAppServerDistribution.RequiredRelativePaths)
        {
            string path = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidDataException("The OpenAI Codex package is incomplete. Repair or update Networker.");
        }

        string manifestPath = Path.Combine(packageRoot, "codex-package.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("layoutVersion", out JsonElement layout) && layout.TryGetInt32(out int layoutVersion)
            && layoutVersion != CodexAppServerDistribution.LayoutVersion)
            throw new InvalidDataException("The OpenAI Codex package layout is unsupported.");
        if (root.TryGetProperty("version", out JsonElement version)
            && version.GetString() is string packageVersion
            && !packageVersion.Equals(CodexAppServerDistribution.Version, StringComparison.Ordinal))
            throw new InvalidDataException("The OpenAI Codex package version does not match this Networker build.");
        if (root.TryGetProperty("variant", out JsonElement variant)
            && variant.GetString() is string packageVariant
            && !packageVariant.Equals(CodexAppServerDistribution.Variant, StringComparison.Ordinal))
            throw new InvalidDataException("The OpenAI Codex package variant is incorrect.");
    }

    private static void VerifyExecutable(string executable)
    {
        // Package identity is validated via codex-package.json + required layout.
        // Individual OpenAI binaries are not re-signed or re-hashed as Networker-owned.
        if (!File.Exists(executable))
            throw new FileNotFoundException("The OpenAI Codex component is missing. Repair or update Networker.", executable);

        string? directory = Path.GetDirectoryName(executable);
        string? packageRoot = directory is null ? null : Path.GetDirectoryName(directory);
        if (packageRoot is null || !File.Exists(Path.Combine(packageRoot, "codex-package.json")))
            throw new InvalidDataException("The OpenAI Codex package layout is invalid.");
    }

    private static object JsonId(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetInt64();
    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();
    private static string Redact(string value) => value.Replace(AppSettings.GetLocalDataDirectory(), "<local-data>", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        await StopProcessAsync().ConfigureAwait(false);
        FailPending(new ObjectDisposedException(nameof(CodexAppServerClient)));
        _lifetime.Dispose();
        _lifecycle.Dispose();
        _writer.Dispose();
    }
}
