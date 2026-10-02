using Gb.Cypher;
using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class QueryGraphService
{
    public GbCypherQueryResult Query(string projectName, string query, int maxRows = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var databasePath = GbCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Project database not found for '{projectName}'.", databasePath);
        }

        using var store = GbStore.OpenPath(databasePath);
        if (store.GetProject(projectName) is null)
        {
            throw new InvalidOperationException($"Project '{projectName}' is not indexed.");
        }

        return CypherExecutor.Execute(store, query, projectName, maxRows);
    }
}
