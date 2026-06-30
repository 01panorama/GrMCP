using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class GraphSchemaService
{
    public CbmGraphSchema GetSchema(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

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

        return store.GetSchemaCounts(projectName);
    }
}
