using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class GraphArchitectureService
{
    public GbArchitectureResult GetArchitecture(
        string projectName,
        string? path = null,
        IReadOnlyList<string>? aspects = null)
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

        return store.GetArchitecture(projectName, path, aspects);
    }
}
