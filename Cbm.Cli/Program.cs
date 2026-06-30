using System.Text.Json;
using System.Text.Json.Serialization;
using Cbm.Cypher;
using Cbm.Graph;
using Cbm.Pipeline;
using System.CommandLine;

var repoPathArgument = new Argument<string>("repo-path")
{
    Description = "Path to a C# repository, solution, or project file.",
};

var indexCommand = new Command("index", "Index a C# repository into the local graph cache.")
{
    repoPathArgument,
};
indexCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var repoPath = parseResult.GetValue(repoPathArgument)!;
    var result = await new IndexRepository().IndexAsync(repoPath, cancellationToken: cancellationToken);
    Console.WriteLine(
        $"Indexed {result.ProjectName}: {result.NodeCount} nodes, {result.EdgeCount} edges -> {result.DatabasePath}");
    return 0;
});

var projectArgument = new Argument<string>("project")
{
    Description = "Indexed project name.",
};

var queryArgument = new Argument<string>("query")
{
    Description = "Cypher query string.",
};

var maxRowsOption = new Option<int>("--max-rows")
{
    Description = "Maximum rows to return (0 = 100k ceiling).",
    DefaultValueFactory = _ => 0,
};

var queryCommand = new Command("query", "Run a Cypher-subset query against an indexed project.")
{
    projectArgument,
    queryArgument,
    maxRowsOption,
};
queryCommand.SetAction((parseResult, _) =>
{
    var project = parseResult.GetValue(projectArgument)!;
    var query = parseResult.GetValue(queryArgument)!;
    var maxRows = parseResult.GetValue(maxRowsOption);

    try
    {
        var result = new QueryGraphService().Query(project, query, maxRows);
        Console.WriteLine(FormatQueryGraphJson(result));
        return Task.FromResult(0);
    }
    catch (Exception ex) when (ex is FileNotFoundException
        or InvalidOperationException
        or CypherParseException
        or CypherPlanException
        or CypherExecuteException)
    {
        Console.Error.WriteLine(ex.Message);
        return Task.FromResult(1);
    }
});

var rootCommand = new RootCommand("CBM .NET tooling");
rootCommand.Subcommands.Add(indexCommand);
rootCommand.Subcommands.Add(queryCommand);

return await rootCommand.Parse(args).InvokeAsync();

static string FormatQueryGraphJson(CbmCypherQueryResult result)
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    return JsonSerializer.Serialize(
        new
        {
            columns = result.Columns,
            rows = result.Rows,
            total = result.Rows.Count,
            hint = result.Hint,
        },
        options);
}
