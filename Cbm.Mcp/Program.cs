using Cbm.Mcp.Tools;
using Cbm.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddSingleton<IndexRepository>();
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
    .WithTools<CbmTools>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
