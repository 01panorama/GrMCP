using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class SearchGraphService
{
    public CbmSearchGraphResult Search(
        string projectName,
        string? query = null,
        string? label = null,
        string? namePattern = null,
        string? qualifiedNamePattern = null,
        string? filePattern = null,
        bool caseSensitive = false,
        int limit = 10,
        int offset = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        using var store = OpenProjectStore(projectName);
        var normalizedLimit = limit <= 0 ? 10 : limit;
        var normalizedOffset = Math.Max(0, offset);
        var total = store.CountSearchNodes(
            projectName,
            query,
            label,
            namePattern,
            qualifiedNamePattern,
            filePattern,
            caseSensitive);
        var results = store.SearchNodes(
            projectName,
            query,
            label,
            namePattern,
            qualifiedNamePattern,
            filePattern,
            caseSensitive,
            normalizedLimit,
            normalizedOffset);

        return new CbmSearchGraphResult(
            results,
            total,
            normalizedOffset,
            normalizedLimit,
            normalizedOffset + results.Count < total);
    }

    private static CbmStore OpenProjectStore(string projectName)
    {
        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Project database not found for '{projectName}'.", databasePath);
        }

        return CbmStore.OpenPath(databasePath);
    }
}
