using Cbm.Graph;
using Cbm.Store;
using Microsoft.Data.Sqlite;

namespace Cbm.Cypher;

public static class CypherExecutor
{
    public const int DefaultMaxRows = CbmStore.DefaultQueryMaxRows;

    private const string ZeroRowHint =
        "Query returned no results. Use get_graph_schema() to see available labels and edge types.";

    public static CbmCypherQueryResult Execute(CbmStore store, string query, string project, int maxRows = 0)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var effectiveMaxRows = maxRows <= 0 ? DefaultMaxRows : maxRows;
        var ast = CypherParserFrontEnd.Parse(query);
        var plan = CypherSqlPlanner.Plan(ast, project);
        var parameters = plan.Parameters.Select(parameter => new KeyValuePair<string, object?>(
            parameter.Name,
            parameter.Value));

        CbmCypherQueryResult result;
        try
        {
            result = store.ExecuteQuery(plan.Sql, parameters, effectiveMaxRows);
        }
        catch (SqliteException ex)
        {
            throw new CypherExecuteException(ex.Message, ex);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("100k", StringComparison.Ordinal))
        {
            throw new CypherExecuteException(ex.Message, ex);
        }

        if (result.Rows.Count == 0)
        {
            return result with { Hint = ZeroRowHint };
        }

        return result;
    }
}
