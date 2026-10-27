using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Gb.Mcp;

public sealed class GbIndexChildProcessRunner
{
    private const int StderrTailLineLimit = 40;

    private static readonly SemaphoreSlim IndexGate = new(1, 1);

    private readonly ILogger<GbIndexChildProcessRunner> logger;

    public GbIndexChildProcessRunner(ILogger<GbIndexChildProcessRunner> logger)
    {
        this.logger = logger;
    }

    public async Task<string> RunIndexAsync(string repoPath, CancellationToken cancellationToken)
    {
        await IndexGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var resultFilePath = Path.Combine(Path.GetTempPath(), $"gb-mcp-index-{Guid.NewGuid():N}.json");
        try
        {
            return await RunIndexCoreAsync(repoPath, resultFilePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IndexGate.Release();
            TryDeleteResultFile(resultFilePath);
        }
    }

    private async Task<string> RunIndexCoreAsync(
        string repoPath,
        string resultFilePath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(repoPath, resultFilePath);
        var stderrLines = new List<string>();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data))
            {
                return;
            }

            lock (stderrLines)
            {
                stderrLines.Add(args.Data);
            }

            logger.LogInformation("{IndexStderr}", args.Data);
        };

        process.OutputDataReceived += (_, _) => { };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start index child process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(resultFilePath))
        {
            throw new McpException(BuildFailureMessage(process.ExitCode, stderrLines));
        }

        return await File.ReadAllTextAsync(resultFilePath, cancellationToken).ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo(string repoPath, string resultFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var value = entry.Value?.ToString();
            if (value is null)
            {
                continue;
            }

            startInfo.Environment[key] = value;
        }

        startInfo.Environment[GbIndexChildArgs.ParentPidEnvironmentVariable] = Environment.ProcessId.ToString();

        var (fileName, arguments) = ResolveSpawnCommand(repoPath, resultFilePath);
        startInfo.FileName = fileName;
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static (string FileName, IReadOnlyList<string> Arguments) ResolveSpawnCommand(
        string repoPath,
        string resultFilePath)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Environment.ProcessPath is not available.");

        var arguments = new List<string>();
        if (IsDotNetHost(processPath))
        {
            arguments.Add("exec");
            arguments.Add(GetEntryAssemblyPath());
        }

        arguments.Add("--index");
        arguments.Add(repoPath);
        arguments.Add("--result-file");
        arguments.Add(resultFilePath);

        return (processPath, arguments);
    }

    private static bool IsDotNetHost(string processPath)
    {
        return string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetEntryAssemblyPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            return location;
        }

        var dllPath = Path.Combine(AppContext.BaseDirectory, "gb-mcp.dll");
        if (File.Exists(dllPath))
        {
            return dllPath;
        }

        throw new InvalidOperationException("Could not resolve gb-mcp entry assembly for index child spawn.");
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort after cancellation.
        }
    }

    private static void TryDeleteResultFile(string resultFilePath)
    {
        try
        {
            if (File.Exists(resultFilePath))
            {
                File.Delete(resultFilePath);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static string BuildFailureMessage(int exitCode, List<string> stderrLines)
    {
        lock (stderrLines)
        {
            var tailStart = Math.Max(0, stderrLines.Count - StderrTailLineLimit);
            var tail = stderrLines.Skip(tailStart);

            var builder = new StringBuilder();
            builder.Append($"index child process failed (exit {exitCode})");
            if (stderrLines.Count > 0)
            {
                builder.Append(": ");
                builder.Append(string.Join(Environment.NewLine, tail));
            }

            return builder.ToString();
        }
    }
}
