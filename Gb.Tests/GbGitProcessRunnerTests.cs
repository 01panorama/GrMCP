using System.Diagnostics;
using Gb.Pipeline;

namespace Gb.Tests;

public sealed class GbGitProcessRunnerTests
{
    [Fact]
    public void RunProcess_CapturesStdoutAndStderrWithoutDeadlock()
    {
        if (!TryCreatePipeFloodStartInfo(out var startInfo))
        {
            return;
        }

        var result = GitProcessRunner.RunProcess(startInfo, TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stdout-head", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stdout-tail", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(result.StandardError.Length >= 65_000);
    }

    private static bool TryCreatePipeFloodStartInfo(out ProcessStartInfo startInfo)
    {
        startInfo = null!;

        if (OperatingSystem.IsWindows())
        {
            var powershell = Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } root
                ? Path.Combine(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
                : null;
            if (powershell is null || !File.Exists(powershell))
            {
                return false;
            }

            const string script =
                """
                Write-Output 'stdout-head'
                $err = 'e' * 70000
                [Console]::Error.Write($err)
                Write-Output 'stdout-tail'
                """;

            startInfo = new ProcessStartInfo
            {
                FileName = powershell,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);
            return true;
        }

        if (!File.Exists("/bin/sh"))
        {
            return false;
        }

        const string shellScript =
            """
            printf 'stdout-head\n'
            i=0
            while [ $i -lt 70000 ]; do
              printf 'e' >&2
              i=$((i + 1))
            done
            printf 'stdout-tail\n'
            """;

        startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(shellScript);
        return true;
    }
}
