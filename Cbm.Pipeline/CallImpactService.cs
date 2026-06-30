using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class CallImpactService
{
    private const int DefaultMaxResults = 500;

    public CbmCallImpactResult Propagate(
        string projectName,
        IReadOnlyList<string> changedRelativePaths,
        int depth = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(changedRelativePaths);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException($"Project '{projectName}' is not indexed.");
        }

        using var store = CbmStore.OpenPath(databasePath);
        var changedSymbols = new List<CbmImpactedSymbol>();
        var seedIds = new List<long>();

        foreach (var relativePath in changedRelativePaths.Distinct(StringComparer.Ordinal))
        {
            foreach (var node in store.FindNodesByFile(projectName, relativePath))
            {
                seedIds.Add(node.Id);
                changedSymbols.Add(new CbmImpactedSymbol(
                    node.Name,
                    node.QualifiedName,
                    node.Label,
                    node.FilePath,
                    Hop: 0,
                    Direction: "changed"));
            }
        }

        var impacted = new Dictionary<long, CbmImpactedSymbol>();
        foreach (var direction in new[] { "inbound", "outbound" })
        {
            foreach (var hop in store.FindImpactedNodes(
                         projectName,
                         seedIds,
                         direction,
                         depth,
                         DefaultMaxResults))
            {
                if (impacted.TryGetValue(hop.Node.Id, out var existing))
                {
                    if (hop.Hop < existing.Hop)
                    {
                        impacted[hop.Node.Id] = new CbmImpactedSymbol(
                            hop.Node.Name,
                            hop.Node.QualifiedName,
                            hop.Node.Label,
                            hop.Node.FilePath,
                            hop.Hop,
                            direction);
                    }

                    continue;
                }

                impacted[hop.Node.Id] = new CbmImpactedSymbol(
                    hop.Node.Name,
                    hop.Node.QualifiedName,
                    hop.Node.Label,
                    hop.Node.FilePath,
                    hop.Hop,
                    direction);
            }
        }

        var impactedSymbols = impacted.Values
            .OrderBy(symbol => symbol.Hop)
            .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();

        return new CbmCallImpactResult(
            changedSymbols.Count,
            impactedSymbols.Length,
            changedSymbols,
            impactedSymbols);
    }
}
