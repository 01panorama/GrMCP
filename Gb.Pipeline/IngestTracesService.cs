using System.Text.Json;
using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class IngestTracesService
{
    public GbIngestTracesResult Ingest(string projectName, IReadOnlyList<JsonElement> traces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(traces);

        var databasePath = GbCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("project not found");
        }

        using var store = GbStore.OpenPath(databasePath);
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
            var (entry, warning) = GbTraceSpanParser.Parse(traceElement);
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

            if (GbTraceSpanParser.HasUnresolvedSymbols(entry, callerNode?.Id, calleeNode?.Id))
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

        return new GbIngestTracesResult(
            Status: "accepted",
            TracesReceived: traces.Count,
            TracesIngested: tracesIngested,
            EdgesMatched: edgesMatched,
            Unresolved: unresolved,
            Warnings: warnings);
    }

    private static GbNode? ResolveNode(GbStore store, string projectName, string symbolName)
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

        var (index, _) = GbTracePathResolver.PickResolvedNode(candidates);
        return candidates[index];
    }
}
