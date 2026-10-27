using Gb.Mcp;
using Gb.Mcp.Tools;
using Gb.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length >= 1 && args[0] == "--emit-docs")
{
    var root = args.Length >= 2 ? args[1] : Directory.GetCurrentDirectory();
    // Renderers already emit LF; write bytes as-is so output matches on macOS and Windows.
    File.WriteAllText(Path.Combine(root, "tools.md"), GbToolCatalog.RenderMarkdown());
    File.WriteAllText(Path.Combine(root, "Skill", "reference.md"), GbSkillReference.RenderMarkdown());
    await Console.Error.WriteLineAsync($"Wrote tools.md and Skill/reference.md to {root}");
    return;
}

if (GbIndexChildArgs.TryParse(args, out var indexRepoPath, out var indexResultFile))
{
    var exitCode = await GbIndexChildEntry.RunAsync(indexRepoPath, indexResultFile);
    Environment.Exit(exitCode);
}

if (args.Any(arg => string.Equals(arg, "--index", StringComparison.Ordinal)
    || string.Equals(arg, "--result-file", StringComparison.Ordinal)))
{
    Environment.Exit(GbIndexChildEntry.WriteUsageAndFail());
}

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddSingleton<GbIndexChildProcessRunner>();
builder.Services.AddSingleton<SearchGraphService>();
builder.Services.AddSingleton<CodeSnippetService>();
builder.Services.AddSingleton<GraphSchemaService>();
builder.Services.AddSingleton<QueryGraphService>();
builder.Services.AddSingleton<GraphArchitectureService>();
builder.Services.AddSingleton<TracePathService>();
builder.Services.AddSingleton<SearchCodeService>();
builder.Services.AddSingleton<ManageAdrService>();
builder.Services.AddSingleton<IngestTracesService>();
builder.Services.AddSingleton<DetectChangesService>();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithRequestFilters(GbToolCallLogging.Configure)
    .WithTools<GbTools>();

builder.Logging.AddFilter(GbToolCallLogging.LoggerCategory, LogLevel.Information);
builder.Logging.AddFilter("Gb.Mcp.GbIndexChildProcessRunner", LogLevel.Information);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
