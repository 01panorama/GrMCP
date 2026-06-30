using Cbm.Cypher;
using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class QueryGraphService
{
    public CbmCypherQueryResult Query(string projectName, string query, int maxRows = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Project database not found for '{projectName}'.", databasePath);
        }

        using var store = CbmStore.OpenPath(databasePath);
        if (store.GetProject(projectName) is null)
        {
            throw new InvalidOperationException($"Project '{projectName}' is not indexed.");
        }

        return CypherExecutor.Execute(store, query, projectName, maxRows);
    }
}
