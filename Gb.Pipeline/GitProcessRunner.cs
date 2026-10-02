using System.ComponentModel;
using System.Diagnostics;

namespace Gb.Pipeline;

internal sealed class GitProcessTimeoutException : Exception
{
    public GitProcessTimeoutException(TimeSpan timeout)
        : base($"Git process did not complete within {timeout.TotalSeconds:0} seconds.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

internal static class GitProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public sealed record GitRunResult(int ExitCode, string StandardOutput, string StandardError);

    public static bool IsGitAvailable()
    {
        try
        {
            var result = RunGlobal(["--version"]);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or GitProcessTimeoutException)
        {
            return false;
        }
    }

    public static GitRunResult RunInRepository(string repositoryRoot, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!GitRefValidator.IsValidRepoPath(repositoryRoot))
        {
            throw new ArgumentException("repository path contains invalid characters", nameof(repositoryRoot));
        }

        var startInfo = CreateGitStartInfo();
        startInfo.ArgumentList.Add("--no-pager");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Path.GetFullPath(repositoryRoot));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunProcess(startInfo);
    }

    /// <summary>
    /// Runs an arbitrary process with the same capture semantics as git invocations (for regression tests).
    /// </summary>
    internal static GitRunResult RunProcess(ProcessStartInfo startInfo, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        startInfo.CreateNoWindow = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start process.");

        process.StandardInput.Close();

        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort after timeout.
            }

            throw new GitProcessTimeoutException(effectiveTimeout);
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new GitRunResult(
            process.ExitCode,
            stdoutTask.Result,
            stderrTask.Result);
    }

    public static IReadOnlyList<string> ReadOutputLines(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var lines = new List<string>();
        using var reader = new StringReader(output);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public static bool TryCapture(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        out string? value)
    {
        value = null;
        try
        {
            var result = RunInRepository(repositoryRoot, arguments);
            if (result.ExitCode != 0)
            {
                return false;
            }

            value = TrimNewlines(result.StandardOutput);
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException or GitProcessTimeoutException)
        {
            return false;
        }
    }

    private static GitRunResult RunGlobal(IReadOnlyList<string> arguments)
    {
        var startInfo = CreateGitStartInfo();
        startInfo.ArgumentList.Add("--no-pager");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunProcess(startInfo);
    }

    private static ProcessStartInfo CreateGitStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return startInfo;
    }

    private static string TrimNewlines(string value)
    {
        return value.TrimEnd('\r', '\n');
    }
}
