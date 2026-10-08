using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Gb.Mcp;

internal static class GbToolCallLogging
{
    internal const string LoggerCategory = "Gb.Mcp.ToolCalls";

    internal static void Configure(IMcpRequestFilterBuilder requestFilters)
    {
        requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?
                .GetService<ILoggerFactory>()?
                .CreateLogger(LoggerCategory);

            var toolName = context.Params?.Name ?? "(unknown)";
            var stopwatch = Stopwatch.StartNew();
            logger?.LogInformation("MCP tool call started: {ToolName}", toolName);

            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                var success = result.IsError != true;
                logger?.LogInformation(
                    "MCP tool call finished: {ToolName} in {ElapsedMs}ms success={Success}",
                    toolName,
                    stopwatch.ElapsedMilliseconds,
                    success);
                return result;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                logger?.LogError(
                    exception,
                    "MCP tool call failed: {ToolName} in {ElapsedMs}ms",
                    toolName,
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        });
    }
}
