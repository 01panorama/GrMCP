using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class GraphSchemaService
{
    public GbGraphSchema GetSchema(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

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

        return store.GetSchemaCounts(projectName);
    }

    public GbGraphSchemaResponse GetSchemaForTool(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

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

        return new GbGraphSchemaResponse(
            store.GetSchemaCounts(projectName),
            store.GetSchemaProperties(projectName));
    }
}
