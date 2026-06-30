using System.Globalization;
using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore
{
    public int DeleteNodesByFiles(string project, IEnumerable<string> relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var paths = relativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
        {
            return 0;
        }

        var placeholders = string.Join(", ", paths.Select((_, index) => "$path" + index));
        using var command = CreateCommand(
            $"""
             DELETE FROM nodes
             WHERE project = $project AND file_path IN ({placeholders});
             """);
        Add(command, "$project", project);
        for (var index = 0; index < paths.Length; index++)
        {
            Add(command, "$path" + index, paths[index]);
        }

        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> ListQualifiedNames(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT qualified_name
            FROM nodes
            WHERE project = $project;
            """);
        Add(command, "$project", project);

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public IReadOnlyDictionary<string, long> BuildIdsByQualifiedName(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT qualified_name, id
            FROM nodes
            WHERE project = $project;
            """);
        Add(command, "$project", project);

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids[reader.GetString(0)] = reader.GetInt64(1);
        }

        return ids;
    }

    public IReadOnlyList<CbmNode> ListNodesByLabels(string project, IReadOnlyList<string> labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(labels);

        var normalizedLabels = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedLabels.Length == 0)
        {
            return [];
        }

        var placeholders = string.Join(", ", normalizedLabels.Select((_, index) => "$label" + index));
        using var command = CreateCommand(
            $"""
             SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
             FROM nodes
             WHERE project = $project AND label IN ({placeholders});
             """);
        Add(command, "$project", project);
        for (var index = 0; index < normalizedLabels.Length; index++)
        {
            Add(command, "$label" + index, normalizedLabels[index]);
        }

        return ReadNodes(command);
    }

    public IReadOnlyList<CbmGraphEdge> ListCallGraphEdges(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT src.qualified_name, tgt.qualified_name, e.type, e.properties
            FROM edges e
            JOIN nodes src ON src.id = e.source_id
            JOIN nodes tgt ON tgt.id = e.target_id
            WHERE e.project = $project AND e.type = 'CALLS';
            """);
        Add(command, "$project", project);

        var edges = new List<CbmGraphEdge>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            edges.Add(new CbmGraphEdge
            {
                Project = project,
                SourceQualifiedName = reader.GetString(0),
                TargetQualifiedName = reader.GetString(1),
                Type = reader.GetString(2),
                PropertiesJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3),
            });
        }

        return edges;
    }

    public IReadOnlyList<CbmSavedGraphEdge> SnapshotInboundCrossFileEdges(
        string project,
        IReadOnlySet<string> changedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(changedPaths);

        if (changedPaths.Count == 0)
        {
            return [];
        }

        var changedList = changedPaths.ToArray();
        var targetPlaceholders = string.Join(", ", changedList.Select((_, index) => "$target" + index));
        var sourcePlaceholders = string.Join(", ", changedList.Select((_, index) => "$source" + index));

        using var command = CreateCommand(
            $"""
             SELECT src.qualified_name, tgt.qualified_name, e.type, e.properties
             FROM edges e
             JOIN nodes src ON src.id = e.source_id
             JOIN nodes tgt ON tgt.id = e.target_id
             WHERE e.project = $project
               AND tgt.file_path IN ({targetPlaceholders})
               AND src.file_path NOT IN ({sourcePlaceholders});
             """);
        Add(command, "$project", project);
        for (var index = 0; index < changedList.Length; index++)
        {
            Add(command, "$target" + index, changedList[index]);
            Add(command, "$source" + index, changedList[index]);
        }

        var edges = new List<CbmSavedGraphEdge>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            edges.Add(new CbmSavedGraphEdge
            {
                SourceQualifiedName = reader.GetString(0),
                TargetQualifiedName = reader.GetString(1),
                Type = reader.GetString(2),
                PropertiesJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3),
            });
        }

        return edges;
    }

    public int RestoreGraphEdges(
        string project,
        IEnumerable<CbmSavedGraphEdge> edges,
        IReadOnlyDictionary<string, long> idsByQualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(idsByQualifiedName);

        var materialized = new List<CbmEdge>();
        foreach (var edge in edges)
        {
            if (!idsByQualifiedName.TryGetValue(edge.SourceQualifiedName, out var sourceId))
            {
                continue;
            }

            if (!idsByQualifiedName.TryGetValue(edge.TargetQualifiedName, out var targetId))
            {
                continue;
            }

            materialized.Add(new CbmEdge
            {
                Project = project,
                SourceId = sourceId,
                TargetId = targetId,
                Type = edge.Type,
                PropertiesJson = edge.PropertiesJson,
            });
        }

        if (materialized.Count == 0)
        {
            return 0;
        }

        UpsertEdgeBatch(materialized);
        return materialized.Count;
    }

    public IReadOnlyList<CbmNodeHop> FindImpactedNodes(
        string project,
        IEnumerable<long> seedNodeIds,
        string direction,
        int depth,
        int maxResults = DefaultBfsResultLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(seedNodeIds);

        var seeds = seedNodeIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (seeds.Length == 0)
        {
            return [];
        }

        var normalizedDepth = Math.Max(1, depth);
        var normalizedLimit = maxResults <= 0 ? DefaultBfsResultLimit : maxResults;
        var isInbound = string.Equals(direction, "inbound", StringComparison.OrdinalIgnoreCase);
        var joinCondition = isInbound
            ? "e.target_id = bfs.node_id"
            : "e.source_id = bfs.node_id";
        var nextIdColumn = isInbound ? "e.source_id" : "e.target_id";
        var seedPlaceholders = string.Join(", ", seeds.Select((_, index) => "$seed" + index));

        var sql =
            $"""
             WITH RECURSIVE bfs(node_id, hop) AS (
               SELECT id, 0 FROM nodes WHERE id IN ({seedPlaceholders})
               UNION
               SELECT {nextIdColumn}, bfs.hop + 1
               FROM bfs
               JOIN edges e ON {joinCondition}
               WHERE e.type = 'CALLS' AND bfs.hop < $maxDepth
             )
             SELECT n.id, n.project, n.label, n.name, n.qualified_name, n.file_path,
                    n.start_line, n.end_line, n.properties, MIN(bfs.hop) AS hop
             FROM bfs
             JOIN nodes n ON n.id = bfs.node_id
             WHERE bfs.hop > 0 AND n.project = $project
             GROUP BY n.id
             ORDER BY hop
             LIMIT $maxResults;
             """;

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        Add(command, "$maxDepth", normalizedDepth);
        Add(command, "$maxResults", normalizedLimit);
        for (var index = 0; index < seeds.Length; index++)
        {
            Add(command, "$seed" + index, seeds[index]);
        }

        var visited = new List<CbmNodeHop>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            visited.Add(new CbmNodeHop(ReadNode(reader), reader.GetInt32(9)));
        }

        return visited;
    }
}
