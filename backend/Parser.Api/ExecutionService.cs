using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

public sealed class ExecutionService
{
    public const int MaxSourceCharacters = 100_000;
    public const int MaxOutputCharacters = 64_000;
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _executionSlot = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);
    private readonly ILogger<ExecutionService> _logger;

    public ExecutionService(ILogger<ExecutionService> logger) => _logger = logger;

    public async Task<ExecutionResponse> ExecuteAsync(ExecuteRequest request, CancellationToken requestAborted)
    {
        if (!Guid.TryParse(request.ExecutionId, out var executionGuid)) throw new ArgumentException("Execution id must be a GUID.");
        var sourceCode = request.SourceCode ?? throw new ArgumentException("Request must include sourceCode.");
        var timeoutSeconds = request.TimeoutSeconds ?? 5;
        if (sourceCode.Length > MaxSourceCharacters) throw new ArgumentException($"Source is limited to {MaxSourceCharacters:N0} characters.");
        if (timeoutSeconds is < 1 or > 15) throw new ArgumentException("Execution timeout must be between 1 and 15 seconds.");
        if (!await _executionSlot.WaitAsync(0, requestAborted)) throw new ExecutionBusyException();

        var id = executionGuid.ToString("N");
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        if (!_active.TryAdd(id, executionCancellation))
        {
            _executionSlot.Release();
            throw new ArgumentException("That execution id is already active.");
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "SimpleParserPlayground");
        var workspace = Path.Combine(workspaceRoot, id);
        var timer = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(workspace);
            var projectPath = Path.Combine(workspace, "Playground.csproj");
            var sourcePath = Path.Combine(workspace, "Program.cs");
            await File.WriteAllTextAsync(projectPath, ProjectFile, new UTF8Encoding(false), executionCancellation.Token);
            await File.WriteAllTextAsync(sourcePath, sourceCode, new UTF8Encoding(false), executionCancellation.Token);

            var build = await RunProcessAsync(workspace, ["build", projectPath, "--configuration", "Release", "--nologo", "--verbosity", "quiet", "-p:NuGetAudit=false"], CompileTimeout, executionCancellation.Token);
            if (build.Cancelled)
                return Result(id, "cancelled", false, build, null, timer.ElapsedMilliseconds);
            if (build.TimedOut)
                return Result(id, "compilation-timeout", false, build, null, timer.ElapsedMilliseconds);
            if (build.ExitCode != 0)
                return Result(id, "compilation-failed", false, build, null, timer.ElapsedMilliseconds);

            executionCancellation.Token.ThrowIfCancellationRequested();
            var assemblyPath = Path.Combine(workspace, "bin", "Release", "net8.0", "Playground.dll");
            var run = await RunProcessAsync(workspace, [assemblyPath], TimeSpan.FromSeconds(timeoutSeconds), executionCancellation.Token, runAssembly: true);
            if (run.Cancelled) return Result(id, "cancelled", true, build, run, timer.ElapsedMilliseconds);
            if (run.TimedOut) return Result(id, "timed-out", true, build, run, timer.ElapsedMilliseconds);
            return Result(id, run.ExitCode == 0 ? "completed" : "runtime-failed", true, build, run, timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            return new ExecutionResponse(id, false, "cancelled", "", "", "", null, timer.ElapsedMilliseconds, false, true, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(exception, "Failed to run execution {ExecutionId}", id);
            return new ExecutionResponse(id, false, "infrastructure-failed", "", exception.Message, "", null, timer.ElapsedMilliseconds, false, false, false);
        }
        finally
        {
            timer.Stop();
            _active.TryRemove(id, out _);
            _executionSlot.Release();
            try
            {
                var fullRoot = Path.GetFullPath(workspaceRoot) + Path.DirectorySeparatorChar;
                var fullWorkspace = Path.GetFullPath(workspace);
                if (fullWorkspace.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullWorkspace))
                    Directory.Delete(fullWorkspace, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not clean temporary execution directory {ExecutionDirectory}", workspace);
            }
        }
    }

    public bool Cancel(string executionId) => _active.TryGetValue(executionId, out var cancellation) && TryCancel(cancellation);

    private static bool TryCancel(CancellationTokenSource cancellation)
    {
        try { cancellation.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    private static ExecutionResponse Result(string id, string status, bool compiled, ProcessResult build, ProcessResult? run, long elapsed) =>
        new(id, compiled, status, run?.StandardOutput ?? string.Empty, run?.StandardError ?? string.Empty,
            JoinDiagnostics(build.StandardOutput, build.StandardError), run?.ExitCode ?? (build.ExitCode == 0 ? null : build.ExitCode),
            elapsed, build.TimedOut || run?.TimedOut == true, build.Cancelled || run?.Cancelled == true,
            build.OutputTruncated || run?.OutputTruncated == true);

    private static string JoinDiagnostics(string output, string error) => string.Join(Environment.NewLine,
        new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));

    private static async Task<ProcessResult> RunProcessAsync(string workingDirectory, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken, bool runAssembly = false)
    {
        var startInfo = CreateStartInfo(workingDirectory, runAssembly);
        if (runAssembly)
        {
            startInfo.ArgumentList.Add(arguments[0]);
        }
        else
        {
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Could not start the .NET child process.");

        var stdoutTask = ReadBoundedAsync(process.StandardOutput);
        var stderrTask = ReadBoundedAsync(process.StandardError);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var timedOut = false;
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled && timeoutSource.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, stdout.Text, stderr.Text, timedOut, cancelled, stdout.Truncated || stderr.Truncated);
    }

    private static ProcessStartInfo CreateStartInfo(string workingDirectory, bool runAssembly)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(host) || !File.Exists(host)) host = "dotnet";
        var startInfo = new ProcessStartInfo(host)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!runAssembly) return startInfo;
        startInfo.Environment.Clear();
        foreach (var name in new[] { "PATH", "SYSTEMROOT", "WINDIR", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "HOME", "APPDATA", "LOCALAPPDATA", "PROGRAMFILES", "PROGRAMFILES(X86)", "NUGET_PACKAGES", "DOTNET_ROOT", "DOTNET_MULTILEVEL_LOOKUP" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null) startInfo.Environment[name] = value;
        }
        return startInfo;
    }

    private static async Task<CapturedText> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var result = new StringBuilder(Math.Min(MaxOutputCharacters, 4096));
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var keep = Math.Min(read, MaxOutputCharacters - result.Length);
            if (keep > 0) result.Append(buffer, 0, keep);
            if (keep < read) truncated = true;
        }
        if (truncated) result.AppendLine().Append("[output truncated at 64,000 characters]");
        return new CapturedText(result.ToString(), truncated);
    }

    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <NuGetAudit>false</NuGetAudit>
          </PropertyGroup>
        </Project>
        """;

    private sealed record CapturedText(string Text, bool Truncated);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool Cancelled, bool OutputTruncated);
}

public sealed class ExecutionBusyException : Exception { }
public sealed record ExecuteRequest(string? SourceCode, string? ExecutionId, int? TimeoutSeconds);
public sealed record CancelExecutionRequest(string? ExecutionId);
public sealed record ExecutionResponse(string ExecutionId, bool CompilationSucceeded, string Status, string StandardOutput,
    string StandardError, string Diagnostics, int? ExitCode, long ElapsedMilliseconds, bool TimedOut, bool Cancelled, bool OutputTruncated);
