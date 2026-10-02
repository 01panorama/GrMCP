using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class TracePathService
{
    private const int DefaultDepth = 3;
    private const string CrossServiceNote =
        "cross_service tracing requires HTTP Route nodes; not indexed in C# port";

    public GbTracePathResult Trace(
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

        var normalizedDirection = GbTracePathResolver.NormalizeDirection(direction);
        var normalizedDepth = GbTracePathResolver.ClampDepth(depth);
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
            return new GbTracePathResult(
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

        var (selectedIndex, ambiguous) = GbTracePathResolver.PickResolvedNode(candidates);
        if (ambiguous)
        {
            var suggestions = candidates
                .Select(node => node.QualifiedName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return new GbTracePathResult(
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
            GbTracePathResolver.ResolveEdgeTypes(resolvedMode, edgeTypes);
        if (isCrossService)
        {
            return BuildCrossServiceResult(functionName, normalizedDirection, resolvedMode);
        }

        var startNode = candidates[selectedIndex];
        var doOutbound = normalizedDirection is "outbound" or "both";
        var doInbound = normalizedDirection is "inbound" or "both";

        IReadOnlyList<GbTraceHop>? callers = null;
        IReadOnlyList<GbTraceHop>? callees = null;

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

        return new GbTracePathResult(
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

    private static GbTracePathResult BuildCrossServiceResult(
        string functionName,
        string direction,
        string mode)
    {
        return new GbTracePathResult(
            Found: true,
            Ambiguous: false,
            FunctionName: functionName,
            Direction: direction,
            Mode: mode,
            Callers: Array.Empty<GbTraceHop>(),
            Callees: Array.Empty<GbTraceHop>(),
            Note: CrossServiceNote,
            Suggestions: null,
            Error: null);
    }

    private static IReadOnlyList<GbTraceHop> MapHops(
        IReadOnlyList<GbNodeHop> visited,
        bool riskLabels,
        bool includeTests)
    {
        var hops = new List<GbTraceHop>();
        foreach (var hop in visited)
        {
            var isTest = GbTracePathResolver.IsTestFile(hop.Node.FilePath);
            if (!includeTests && isTest)
            {
                continue;
            }

            hops.Add(new GbTraceHop(
                Name: hop.Node.Name,
                QualifiedName: hop.Node.QualifiedName,
                Hop: hop.Hop,
                Risk: riskLabels ? GbStore.HopToRiskLabel(hop.Hop) : null,
                IsTest: isTest ? true : null));
        }

        return hops;
    }
}
