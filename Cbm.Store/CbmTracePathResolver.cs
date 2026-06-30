using Cbm.Graph;

namespace Cbm.Store;

public static class CbmTracePathResolver
{
    public const string CrossServiceMode = "cross_service";

    private const int LabelWeight = 1_000_000;

    public static (int Index, bool Ambiguous) PickResolvedNode(IReadOnlyList<CbmNode> nodes)
    {
        if (nodes.Count <= 1)
        {
            return (0, false);
        }

        var best = 0;
        var bestScore = NodeResolutionScore(nodes[0]);
        for (var i = 1; i < nodes.Count; i++)
        {
            var score = NodeResolutionScore(nodes[i]);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        var topCount = nodes.Count(node => NodeResolutionScore(node) == bestScore);
        return (best, topCount > 1);
    }

    public static bool IsTestFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.Contains("/test", StringComparison.Ordinal)
            || path.Contains("test_", StringComparison.Ordinal)
            || path.Contains("_test.", StringComparison.Ordinal)
            || path.Contains("/tests/", StringComparison.Ordinal)
            || path.Contains("/spec/", StringComparison.Ordinal)
            || path.Contains(".test.", StringComparison.Ordinal);
    }

    public static (IReadOnlyList<string> EdgeTypes, bool IsCrossService) ResolveEdgeTypes(
        string? mode,
        IReadOnlyList<string>? explicitTypes)
    {
        if (explicitTypes is { Count: > 0 })
        {
            return (explicitTypes, false);
        }

        if (string.Equals(mode, CrossServiceMode, StringComparison.Ordinal))
        {
            return (Array.Empty<string>(), true);
        }

        if (string.Equals(mode, "data_flow", StringComparison.Ordinal))
        {
            return (["CALLS", "USAGE", "WRITES"], false);
        }

        return (["CALLS"], false);
    }

    public static string NormalizeDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return "both";
        }

        return direction.Trim().ToLowerInvariant() switch
        {
            "inbound" => "inbound",
            "outbound" => "outbound",
            "both" => "both",
            _ => "both",
        };
    }

    public static int ClampDepth(int depth)
    {
        if (depth < 1)
        {
            return 1;
        }

        return depth > 5 ? 5 : depth;
    }

    private static long NodeResolutionScore(CbmNode node)
    {
        var labelRank = node.Label switch
        {
            "Method" or "Constructor" => 2,
            "File" or "Namespace" => 0,
            _ => 1,
        };

        var span = node.EndLine - node.StartLine;
        if (span < 0)
        {
            span = 0;
        }

        return labelRank * LabelWeight + span;
    }
}
