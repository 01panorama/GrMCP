using System.Diagnostics;
using Gb.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gb.Mcp;

internal static class GbIndexChildEntry
{
    private const string Usage =
        "Usage: gb-mcp --index <repo_path> --result-file <path>";

    internal static async Task<int> RunAsync(string repoPath, string resultFilePath)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            builder.AddFilter("Gb.Pipeline.IndexRepository", LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<IndexRepository>();
        using var parentWatcher = StartParentProcessWatcher();

        try
        {
            var indexRepository = new IndexRepository(logger);
            var result = await indexRepository.IndexAsync(repoPath).ConfigureAwait(false);
            await File.WriteAllTextAsync(resultFilePath, GbMcpJson.FormatIndexResult(result)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    internal static int WriteUsageAndFail()
    {
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static IDisposable? StartParentProcessWatcher()
    {
        var parentPidText = Environment.GetEnvironmentVariable(GbIndexChildArgs.ParentPidEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(parentPidText) || !int.TryParse(parentPidText, out var parentPid))
        {
            return null;
        }

        var cts = new CancellationTokenSource();
        var watchTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (!IsProcessAlive(parentPid))
                {
                    Environment.Exit(1);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);

        return new ParentWatcher(cts, watchTask);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private sealed class ParentWatcher(CancellationTokenSource cts, Task watchTask) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            try
            {
                watchTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best effort shutdown.
            }

            cts.Dispose();
        }
    }
}
