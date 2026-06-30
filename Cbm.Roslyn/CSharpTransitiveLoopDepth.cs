using System.Text.Json;
using Cbm.Graph;

namespace Cbm.Roslyn;

public static class CSharpTransitiveLoopDepth
{
    public static IReadOnlyList<CbmNode> Apply(
        IReadOnlyList<CbmNode> nodes,
        IReadOnlyList<CbmGraphEdge> edges)
    {
        var localLoopDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.Label is not ("Method" or "Constructor"))
            {
                continue;
            }

            localLoopDepth[node.QualifiedName] = ReadLoopDepth(node.PropertiesJson);
        }

        if (localLoopDepth.Count == 0)
        {
            return nodes;
        }

        var callGraph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!string.Equals(edge.Type, "CALLS", StringComparison.Ordinal))
            {
                continue;
            }

            if (!callGraph.TryGetValue(edge.SourceQualifiedName, out var callees))
            {
                callees = [];
                callGraph[edge.SourceQualifiedName] = callees;
            }

            callees.Add(edge.TargetQualifiedName);
        }

        var transitiveDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var qualifiedName in localLoopDepth.Keys)
        {
            ComputeTransitiveDepth(qualifiedName, localLoopDepth, callGraph, transitiveDepth, []);
        }

        var updatedNodes = new List<CbmNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (!transitiveDepth.TryGetValue(node.QualifiedName, out var depth) || depth <= 0)
            {
                updatedNodes.Add(node);
                continue;
            }

            updatedNodes.Add(node with
            {
                PropertiesJson = WriteTransitiveLoopDepth(node.PropertiesJson, depth),
            });
        }

        return updatedNodes;
    }

    private static int ComputeTransitiveDepth(
        string qualifiedName,
        IReadOnlyDictionary<string, int> localLoopDepth,
        IReadOnlyDictionary<string, List<string>> callGraph,
        Dictionary<string, int> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(qualifiedName, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(qualifiedName))
        {
            return localLoopDepth.GetValueOrDefault(qualifiedName);
        }

        var depth = localLoopDepth.GetValueOrDefault(qualifiedName);
        if (callGraph.TryGetValue(qualifiedName, out var callees))
        {
            foreach (var callee in callees)
            {
                if (!localLoopDepth.ContainsKey(callee))
                {
                    continue;
                }

                var calleeDepth = ComputeTransitiveDepth(
                    callee,
                    localLoopDepth,
                    callGraph,
                    memo,
                    visiting);
                depth = Math.Max(depth, calleeDepth);
            }
        }

        visiting.Remove(qualifiedName);
        memo[qualifiedName] = depth;
        return depth;
    }

    private static int ReadLoopDepth(string propertiesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(propertiesJson);
            if (document.RootElement.TryGetProperty("loop_depth", out var property))
            {
                return property.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return 0;
    }

    private static string WriteTransitiveLoopDepth(string propertiesJson, int depth)
    {
        var properties = new Dictionary<string, JsonElement>();
        try
        {
            using var document = JsonDocument.Parse(propertiesJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
        }

        properties["transitive_loop_depth"] = JsonSerializer.SerializeToElement(depth);
        return JsonSerializer.Serialize(properties);
    }
}
