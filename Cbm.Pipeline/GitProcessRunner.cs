using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Cbm.Pipeline;

internal static class GitProcessRunner
{
    public sealed record GitRunResult(int ExitCode, string StandardOutput, string StandardError);

    public static bool IsGitAvailable()
    {
        try
        {
            var result = RunGlobal(["--version"]);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
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

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Path.GetFullPath(repositoryRoot));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitRunResult(process.ExitCode, stdout, stderr);
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
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static GitRunResult RunGlobal(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitRunResult(process.ExitCode, stdout, stderr);
    }

    private static string TrimNewlines(string value)
    {
        return value.TrimEnd('\r', '\n');
    }
}
