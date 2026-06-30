using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore
{
    private const int DefaultBfsResultLimit = 100;

    public IReadOnlyList<CbmNode> FindNodesByName(string project, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var command = CreateCommand(
            """
            SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
            FROM nodes
            WHERE project = $project AND name = $name
            ORDER BY qualified_name;
            """);
        Add(command, "$project", project);
        Add(command, "$name", name);
        return ReadNodes(command);
    }

    public CbmTraverseResult Bfs(
        long startId,
        string direction,
        IReadOnlyList<string>? edgeTypes,
        int maxDepth,
        int maxResults = DefaultBfsResultLimit)
    {
        var root = FindNodeById(startId)
            ?? throw new InvalidOperationException($"Node {startId} was not found.");

        var normalizedTypes = NormalizeEdgeTypes(edgeTypes);
        var normalizedDepth = Math.Max(1, maxDepth);
        var normalizedLimit = maxResults <= 0 ? DefaultBfsResultLimit : maxResults;
        var isInbound = string.Equals(direction, "inbound", StringComparison.OrdinalIgnoreCase);

        var joinCondition = isInbound
            ? "e.target_id = bfs.node_id"
            : "e.source_id = bfs.node_id";
        var nextIdColumn = isInbound ? "e.source_id" : "e.target_id";
        var typePlaceholders = string.Join(", ", normalizedTypes.Select((_, index) => "$type" + index));

        var sql =
            $"""
             WITH RECURSIVE bfs(node_id, hop) AS (
               SELECT $startId, 0
               UNION
               SELECT {nextIdColumn}, bfs.hop + 1
               FROM bfs
               JOIN edges e ON {joinCondition}
               WHERE e.type IN ({typePlaceholders}) AND bfs.hop < $maxDepth
             )
             SELECT DISTINCT n.id, n.project, n.label, n.name, n.qualified_name, n.file_path,
                    n.start_line, n.end_line, n.properties, bfs.hop
             FROM bfs
             JOIN nodes n ON n.id = bfs.node_id
             WHERE bfs.hop > 0
             ORDER BY bfs.hop
             LIMIT $maxResults;
             """;

        using var command = CreateCommand(sql);
        Add(command, "$startId", startId);
        Add(command, "$maxDepth", normalizedDepth);
        Add(command, "$maxResults", normalizedLimit);
        for (var i = 0; i < normalizedTypes.Count; i++)
        {
            Add(command, "$type" + i, normalizedTypes[i]);
        }

        var visited = new List<CbmNodeHop>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            visited.Add(new CbmNodeHop(ReadNode(reader), reader.GetInt32(9)));
        }

        return new CbmTraverseResult(root, visited);
    }

    public static string HopToRiskLabel(int hop)
    {
        return hop switch
        {
            1 => "CRITICAL",
            2 => "HIGH",
            3 => "MEDIUM",
            _ => "LOW",
        };
    }

    private static IReadOnlyList<string> NormalizeEdgeTypes(IReadOnlyList<string>? edgeTypes)
    {
        if (edgeTypes is null || edgeTypes.Count == 0)
        {
            return ["CALLS"];
        }

        return edgeTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
