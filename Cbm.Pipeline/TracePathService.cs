using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class TracePathService
{
    private const int DefaultDepth = 3;
    private const string CrossServiceNote =
        "cross_service tracing requires HTTP Route nodes; not indexed in C# port";

    public CbmTracePathResult Trace(
        string projectName,
        string functionName,
        string direction = "both",
        string? mode = null,
        int depth = DefaultDepth,
        bool riskLabels = false,
        bool includeTests = false,
        IReadOnlyList<string>? edgeTypes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

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

        var normalizedDirection = CbmTracePathResolver.NormalizeDirection(direction);
        var normalizedDepth = CbmTracePathResolver.ClampDepth(depth);
        var resolvedMode = string.IsNullOrWhiteSpace(mode) ? "calls" : mode.Trim();

        var candidates = store.FindNodesByName(projectName, functionName);
        if (candidates.Count == 0)
        {
            var exactQualifiedName = store.FindNodeByQualifiedName(projectName, functionName);
            if (exactQualifiedName is not null)
            {
                candidates = [exactQualifiedName];
            }
        }

        if (candidates.Count == 0)
        {
            return new CbmTracePathResult(
                Found: false,
                Ambiguous: false,
                FunctionName: functionName,
                Direction: normalizedDirection,
                Mode: resolvedMode,
                Callers: null,
                Callees: null,
                Note: null,
                Suggestions: null,
                Error:
                    $"function not found. Use search_graph(name_pattern=\".*{functionName}.*\") to find the exact name, then pass it to trace_path.");
        }

        var (selectedIndex, ambiguous) = CbmTracePathResolver.PickResolvedNode(candidates);
        if (ambiguous)
        {
            var suggestions = candidates
                .Select(node => node.QualifiedName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return new CbmTracePathResult(
                Found: false,
                Ambiguous: true,
                FunctionName: functionName,
                Direction: normalizedDirection,
                Mode: resolvedMode,
                Callers: null,
                Callees: null,
                Note: null,
                Suggestions: suggestions,
                Error: "multiple symbols match; pass an exact qualified_name");
        }

        var (resolvedEdgeTypes, isCrossService) =
            CbmTracePathResolver.ResolveEdgeTypes(resolvedMode, edgeTypes);
        if (isCrossService)
        {
            return BuildCrossServiceResult(functionName, normalizedDirection, resolvedMode);
        }

        var startNode = candidates[selectedIndex];
        var doOutbound = normalizedDirection is "outbound" or "both";
        var doInbound = normalizedDirection is "inbound" or "both";

        IReadOnlyList<CbmTraceHop>? callers = null;
        IReadOnlyList<CbmTraceHop>? callees = null;

        if (doInbound)
        {
            var inbound = store.Bfs(
                startNode.Id,
                "inbound",
                resolvedEdgeTypes,
                normalizedDepth);
            callers = MapHops(inbound.Visited, riskLabels, includeTests);
        }

        if (doOutbound)
        {
            var outbound = store.Bfs(
                startNode.Id,
                "outbound",
                resolvedEdgeTypes,
                normalizedDepth);
            callees = MapHops(outbound.Visited, riskLabels, includeTests);
        }

        return new CbmTracePathResult(
            Found: true,
            Ambiguous: false,
            FunctionName: functionName,
            Direction: normalizedDirection,
            Mode: resolvedMode,
            Callers: callers,
            Callees: callees,
            Note: null,
            Suggestions: null,
            Error: null);
    }

    private static CbmTracePathResult BuildCrossServiceResult(
        string functionName,
        string direction,
        string mode)
    {
        return new CbmTracePathResult(
            Found: true,
            Ambiguous: false,
            FunctionName: functionName,
            Direction: direction,
            Mode: mode,
            Callers: Array.Empty<CbmTraceHop>(),
            Callees: Array.Empty<CbmTraceHop>(),
            Note: CrossServiceNote,
            Suggestions: null,
            Error: null);
    }

    private static IReadOnlyList<CbmTraceHop> MapHops(
        IReadOnlyList<CbmNodeHop> visited,
        bool riskLabels,
        bool includeTests)
    {
        var hops = new List<CbmTraceHop>();
        foreach (var hop in visited)
        {
            var isTest = CbmTracePathResolver.IsTestFile(hop.Node.FilePath);
            if (!includeTests && isTest)
            {
                continue;
            }

            hops.Add(new CbmTraceHop(
                Name: hop.Node.Name,
                QualifiedName: hop.Node.QualifiedName,
                Hop: hop.Hop,
                Risk: riskLabels ? CbmStore.HopToRiskLabel(hop.Hop) : null,
                IsTest: isTest ? true : null));
        }

        return hops;
    }
}
