using System.Text.Json;
using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class IngestTracesService
{
    public CbmIngestTracesResult Ingest(string projectName, IReadOnlyList<JsonElement> traces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(traces);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("project not found");
        }

        using var store = CbmStore.OpenPath(databasePath);
        if (store.GetProject(projectName) is null)
        {
            throw new InvalidOperationException("project not found");
        }

        var warnings = new List<string>();
        var tracesIngested = 0;
        var edgesMatched = 0;
        var unresolved = 0;

        foreach (var traceElement in traces)
        {
            var (entry, warning) = CbmTraceSpanParser.Parse(traceElement);
            if (warning is not null)
            {
                warnings.Add(warning);
            }

            if (entry is null)
            {
                continue;
            }

            var callerNode = ResolveNode(store, projectName, entry.Caller);
            var calleeNode = ResolveNode(store, projectName, entry.Callee);
            long? callsEdgeId = null;

            if (callerNode is not null && calleeNode is not null)
            {
                callsEdgeId = store.FindCallsEdge(projectName, callerNode.Id, calleeNode.Id);
                if (callsEdgeId is not null)
                {
                    edgesMatched++;
                }
            }

            if (CbmTraceSpanParser.HasUnresolvedSymbols(entry, callerNode?.Id, calleeNode?.Id))
            {
                unresolved++;
            }

            store.TraceObservationUpsert(
                projectName,
                entry,
                callerNode?.Id,
                calleeNode?.Id,
                callsEdgeId);
            tracesIngested++;
        }

        return new CbmIngestTracesResult(
            Status: "accepted",
            TracesReceived: traces.Count,
            TracesIngested: tracesIngested,
            EdgesMatched: edgesMatched,
            Unresolved: unresolved,
            Warnings: warnings);
    }

    private static CbmNode? ResolveNode(CbmStore store, string projectName, string symbolName)
    {
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return null;
        }

        var candidates = store.FindNodesByName(projectName, symbolName);
        if (candidates.Count == 0)
        {
            return store.FindNodeByQualifiedName(projectName, symbolName);
        }

        var (index, _) = CbmTracePathResolver.PickResolvedNode(candidates);
        return candidates[index];
    }
}
