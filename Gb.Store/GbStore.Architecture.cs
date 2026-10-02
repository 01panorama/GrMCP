using System.Globalization;
using Gb.Graph;
using Microsoft.Data.Sqlite;

namespace Gb.Store;

public sealed partial class GbStore
{
    private const string CallsEdgeTypeArch = "CALLS";
    private const int ClusterTopN = 12;
    private const int ClusterMaxTopNodes = 5;
    private const int ClusterMaxPkgs = 5;
    private const int ClusterMinMembers = 2;
    private const int ClusterNodeCap = 8000;
    private const int MaxBoundaries = 10;
    private const int MaxPreviewPackages = 15;
    private const int MinIndegreeForCore = 3;

    private static readonly string[] ArchitectureNodeLabels =
        ["Method", "Constructor", "Class"];

    public int CountNodesScoped(string project, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        if (!GbArchitecturePath.TryPrepare(path, out var normalized, out var likePattern))
        {
            return CountNodes(project);
        }

        using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM nodes
            WHERE project = $project
              AND (file_path = $scopePath OR file_path LIKE $scopeLike);
            """);
        Add(command, "$project", project);
        Add(command, "$scopePath", normalized);
        Add(command, "$scopeLike", likePattern);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int CountEdgesScoped(string project, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        if (!GbArchitecturePath.TryPrepare(path, out var normalized, out var likePattern))
        {
            return CountEdges(project);
        }

        using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM edges e
            WHERE e.project = $project
              AND EXISTS (
                SELECT 1 FROM nodes ns
                WHERE ns.id = e.source_id AND ns.project = $project
                  AND (ns.file_path = $scopePath OR ns.file_path LIKE $scopeLike))
              AND EXISTS (
                SELECT 1 FROM nodes nt
                WHERE nt.id = e.target_id AND nt.project = $project
                  AND (nt.file_path = $scopePath OR nt.file_path LIKE $scopeLike));
            """);
        Add(command, "$project", project);
        Add(command, "$scopePath", normalized);
        Add(command, "$scopeLike", likePattern);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public GbGraphSchema GetSchemaCountsScoped(string project, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        if (!GbArchitecturePath.TryPrepare(path, out var normalized, out var likePattern))
        {
            return GetSchemaCounts(project);
        }

        var labels = new List<GbLabelSchema>();
        using (var command = CreateCommand(
                   $"""
                    SELECT label, COUNT(*)
                    FROM nodes
                    WHERE project = $project{GbArchitecturePath.ScopeSql}
                    GROUP BY label
                    ORDER BY COUNT(*) DESC, label;
                    """))
        {
            Add(command, "$project", project);
            Add(command, "$scopePath", normalized);
            Add(command, "$scopeLike", likePattern);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                labels.Add(new GbLabelSchema(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        var edgeTypes = new List<GbEdgeTypeSchema>();
        using (var command = CreateCommand(
                   """
                   SELECT e.type, COUNT(*)
                   FROM edges e
                   WHERE e.project = $project
                     AND EXISTS (
                       SELECT 1 FROM nodes ns
                       WHERE ns.id = e.source_id AND ns.project = $project
                         AND (ns.file_path = $scopePath OR ns.file_path LIKE $scopeLike))
                     AND EXISTS (
                       SELECT 1 FROM nodes nt
                       WHERE nt.id = e.target_id AND nt.project = $project
                         AND (nt.file_path = $scopePath OR nt.file_path LIKE $scopeLike))
                   GROUP BY e.type
                   ORDER BY COUNT(*) DESC, e.type;
                   """))
        {
            Add(command, "$project", project);
            Add(command, "$scopePath", normalized);
            Add(command, "$scopeLike", likePattern);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                edgeTypes.Add(new GbEdgeTypeSchema(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        return new GbGraphSchema(labels, edgeTypes);
    }

    public GbArchitectureResult GetArchitecture(
        string project,
        string? path = null,
        IReadOnlyList<string>? aspects = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var pathScoped = GbArchitecturePath.TryPrepare(path, out var normalizedPath, out var likePattern);
        var totalNodes = CountNodesScoped(project, path);
        var totalEdges = CountEdgesScoped(project, path);
        int? rootTotalNodes = pathScoped ? CountNodes(project) : null;
        int? rootTotalEdges = pathScoped ? CountEdges(project) : null;

        GbGraphSchema? structure = null;
        GbGraphSchema? dependencies = null;

        if (WantsAspect(aspects, "structure") || WantsAspect(aspects, "dependencies"))
        {
            var schema = GetSchemaCountsScoped(project, path);
            if (WantsAspect(aspects, "structure"))
            {
                structure = new GbGraphSchema(schema.NodeLabels, []);
            }

            if (WantsAspect(aspects, "dependencies"))
            {
                dependencies = new GbGraphSchema([], schema.EdgeTypes);
            }
        }

        List<GbCrossPackageBoundary>? boundaries = null;
        if (WantsAspect(aspects, "boundaries") || WantsAspect(aspects, "layers")
            || WantsAspect(aspects, "packages"))
        {
            boundaries = LoadBoundaries(project, pathScoped ? normalizedPath : null, pathScoped ? likePattern : null);
        }

        IReadOnlyList<GbLanguageCount>? languages = WantsAspect(aspects, "languages")
            ? LoadLanguages(project, pathScoped, normalizedPath, likePattern)
            : null;

        IReadOnlyList<GbPackageSummary>? packages = WantsAspect(aspects, "packages")
            ? LoadPackages(project, pathScoped, normalizedPath, likePattern, boundaries)
            : null;

        IReadOnlyList<GbEntryPoint>? entryPoints = WantsAspect(aspects, "entry_points")
            ? LoadEntryPoints(project, pathScoped, normalizedPath, likePattern)
            : null;

        IReadOnlyList<GbHotspot>? hotspots = WantsAspect(aspects, "hotspots")
            ? LoadHotspots(project, pathScoped, normalizedPath, likePattern)
            : null;

        IReadOnlyList<GbCrossPackageBoundary>? boundaryResult = WantsAspect(aspects, "boundaries")
            ? boundaries
            : null;

        IReadOnlyList<GbPackageLayer>? layers = WantsAspect(aspects, "layers")
            ? LoadLayers(project, pathScoped, normalizedPath, likePattern, boundaries!)
            : null;

        IReadOnlyList<GbClusterInfo>? clusters = WantsAspect(aspects, "clusters")
            ? LoadClusters(project, pathScoped, normalizedPath, likePattern)
            : null;

        IReadOnlyList<GbFileTreeEntry>? fileTree = WantsAspect(aspects, "file_tree")
            ? LoadFileTree(project, pathScoped, normalizedPath, likePattern)
            : null;

        GbRuntimeSummary? runtime = WantsAspect(aspects, "runtime")
            ? new GbRuntimeSummary(
                CountRuntimeObservations(project),
                CountMatchedRuntimeEdges(project),
                ListRuntimeObservations(project))
            : null;

        return new GbArchitectureResult(
            Project: project,
            Path: pathScoped ? normalizedPath : null,
            TotalNodes: totalNodes,
            TotalEdges: totalEdges,
            RootTotalNodes: rootTotalNodes,
            RootTotalEdges: rootTotalEdges,
            Structure: structure,
            Dependencies: dependencies,
            Languages: languages,
            Packages: packages,
            EntryPoints: entryPoints,
            Hotspots: hotspots,
            Boundaries: boundaryResult,
            Layers: layers,
            Clusters: clusters,
            FileTree: fileTree,
            Runtime: runtime);
    }

    private static bool WantsAspect(IReadOnlyList<string>? aspects, string name)
    {
        if (aspects is null || aspects.Count == 0)
        {
            return true;
        }

        foreach (var aspect in aspects)
        {
            if (aspect == "all" || string.Equals(aspect, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void BindPathScope(SqliteCommand command, string normalized, string likePattern)
    {
        Add(command, "$scopePath", normalized);
        Add(command, "$scopeLike", likePattern);
    }

    private IReadOnlyList<GbLanguageCount> LoadLanguages(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var sql = scoped
            ? $"""
               SELECT COUNT(DISTINCT file_path)
               FROM nodes
               WHERE project = $project AND label = 'File'{GbArchitecturePath.ScopeSql};
               """
            : """
              SELECT COUNT(DISTINCT file_path)
              FROM nodes
              WHERE project = $project AND label = 'File';
              """;

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return count > 0
            ? [new GbLanguageCount("C#", count)]
            : Array.Empty<GbLanguageCount>();
    }

    private IReadOnlyList<GbEntryPoint> LoadEntryPoints(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT name, qualified_name, file_path
            FROM nodes
            WHERE project = $project
              AND json_extract(properties, '$.is_entry_point') = 1
              AND (json_extract(properties, '$.is_test') IS NULL OR json_extract(properties, '$.is_test') != 1)
              AND file_path NOT LIKE '%test%'
            """;
        var sql = scoped
            ? baseSql + GbArchitecturePath.ScopeSql + " LIMIT 20"
            : baseSql + " LIMIT 20";

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var results = new List<GbEntryPoint>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new GbEntryPoint(
                GetString(reader, 0),
                GetString(reader, 1),
                GetString(reader, 2)));
        }

        return results;
    }

    private IReadOnlyList<GbHotspot> LoadHotspots(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT n.name, n.qualified_name, COUNT(*) AS fan_in
            FROM nodes n
            JOIN edges e ON e.target_id = n.id AND e.type = 'CALLS'
            WHERE n.project = $project
              AND n.label IN ('Method', 'Constructor')
              AND (json_extract(n.properties, '$.is_test') IS NULL OR json_extract(n.properties, '$.is_test') != 1)
              AND n.file_path NOT LIKE '%test%'
            """;
        var sql = scoped
            ? baseSql + GbArchitecturePath.ScopeSql + " GROUP BY n.id ORDER BY fan_in DESC LIMIT 10"
            : baseSql + " GROUP BY n.id ORDER BY fan_in DESC LIMIT 10";

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var results = new List<GbHotspot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new GbHotspot(
                GetString(reader, 0),
                GetString(reader, 1),
                reader.GetInt32(2)));
        }

        return results;
    }

    private List<GbCrossPackageBoundary> LoadBoundaries(
        string project,
        string? normalized,
        string? likePattern)
    {
        var scoped = normalized is not null && likePattern is not null;
        var labelList = string.Join(", ", ArchitectureNodeLabels.Select(l => $"'{l}'"));
        var nodeSql = scoped
            ? $"""
               SELECT id, qualified_name
               FROM nodes
               WHERE project = $project AND label IN ({labelList}){GbArchitecturePath.ScopeSql}
               ORDER BY id;
               """
            : $"""
               SELECT id, qualified_name
               FROM nodes
               WHERE project = $project AND label IN ({labelList})
               ORDER BY id;
               """;

        var nodeIds = new List<long>();
        var packages = new List<string>();

        using (var command = CreateCommand(nodeSql))
        {
            Add(command, "$project", project);
            if (scoped)
            {
                BindPathScope(command, normalized!, likePattern!);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                nodeIds.Add(reader.GetInt64(0));
                packages.Add(GbQualifiedNameHelpers.QnToPackage(GetString(reader, 1)));
            }
        }

        var boundaryMap = new Dictionary<(string From, string To), int>();

        using (var command = CreateCommand(
                   """
                   SELECT source_id, target_id
                   FROM edges
                   WHERE project = $project AND type = 'CALLS';
                   """))
        {
            Add(command, "$project", project);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var srcId = reader.GetInt64(0);
                var tgtId = reader.GetInt64(1);
                var srcIdx = nodeIds.BinarySearch(srcId);
                var tgtIdx = nodeIds.BinarySearch(tgtId);
                if (srcIdx < 0 || tgtIdx < 0)
                {
                    continue;
                }

                var srcPkg = packages[srcIdx];
                var tgtPkg = packages[tgtIdx];
                if (string.IsNullOrEmpty(srcPkg) || string.IsNullOrEmpty(tgtPkg) || srcPkg == tgtPkg)
                {
                    continue;
                }

                var key = (srcPkg, tgtPkg);
                boundaryMap.TryGetValue(key, out var count);
                boundaryMap[key] = count + 1;
            }
        }

        return boundaryMap
            .OrderByDescending(pair => pair.Value)
            .Take(MaxBoundaries)
            .Select(pair => new GbCrossPackageBoundary(pair.Key.From, pair.Key.To, pair.Value))
            .ToList();
    }

    private IReadOnlyList<GbPackageSummary> LoadPackages(
        string project,
        bool scoped,
        string normalized,
        string likePattern,
        List<GbCrossPackageBoundary>? boundaries)
    {
        var packages = LoadNamespacePackages(project, scoped, normalized, likePattern);
        if (packages.Count == 0)
        {
            packages = LoadPackageLabelNodes(project, scoped, normalized, likePattern);
        }

        if (packages.Count == 0)
        {
            packages = LoadPackagesFromQualifiedNames(project, scoped, normalized, likePattern);
        }

        if (boundaries is not null && packages.Count > 0)
        {
            var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
            var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var boundary in boundaries)
            {
                fanOut.TryGetValue(boundary.From, out var outCount);
                fanOut[boundary.From] = outCount + boundary.CallCount;
                fanIn.TryGetValue(boundary.To, out var inCount);
                fanIn[boundary.To] = inCount + boundary.CallCount;
            }

            packages = packages
                .Select(pkg => pkg with
                {
                    FanIn = fanIn.GetValueOrDefault(pkg.Name),
                    FanOut = fanOut.GetValueOrDefault(pkg.Name),
                })
                .ToList();
        }

        return packages;
    }

    private List<GbPackageSummary> LoadNamespacePackages(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT n.name, COUNT(*) AS cnt
            FROM nodes n
            WHERE n.project = $project AND n.label = 'Namespace'
            """;
        var sql = scoped
            ? baseSql + GbArchitecturePath.ScopeSql + " GROUP BY n.name ORDER BY cnt DESC LIMIT 15"
            : baseSql + " GROUP BY n.name ORDER BY cnt DESC LIMIT 15";

        return QueryPackageSummaries(project, scoped, normalized, likePattern, sql);
    }

    private List<GbPackageSummary> LoadPackageLabelNodes(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT n.name, COUNT(*) AS cnt
            FROM nodes n
            WHERE n.project = $project AND n.label = 'Package'
            """;
        var sql = scoped
            ? baseSql + GbArchitecturePath.ScopeSql + " GROUP BY n.name ORDER BY cnt DESC LIMIT 15"
            : baseSql + " GROUP BY n.name ORDER BY cnt DESC LIMIT 15";

        return QueryPackageSummaries(project, scoped, normalized, likePattern, sql);
    }

    private List<GbPackageSummary> QueryPackageSummaries(
        string project,
        bool scoped,
        string normalized,
        string likePattern,
        string sql)
    {
        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var results = new List<GbPackageSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new GbPackageSummary(
                GetString(reader, 0),
                reader.GetInt32(1),
                FanIn: 0,
                FanOut: 0));
        }

        return results;
    }

    private List<GbPackageSummary> LoadPackagesFromQualifiedNames(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var labelList = string.Join(", ", ArchitectureNodeLabels.Select(l => $"'{l}'"));
        var baseSql = $"""
            SELECT qualified_name
            FROM nodes
            WHERE project = $project AND label IN ({labelList})
            """;
        var sql = scoped ? baseSql + GbArchitecturePath.ScopeSql : baseSql;

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var pkg = GbQualifiedNameHelpers.QnToPackage(GetString(reader, 0));
            if (string.IsNullOrEmpty(pkg))
            {
                continue;
            }

            counts.TryGetValue(pkg, out var count);
            counts[pkg] = count + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .Take(MaxPreviewPackages)
            .Select(pair => new GbPackageSummary(pair.Key, pair.Value, 0, 0))
            .ToList();
    }

    private IReadOnlyList<GbPackageLayer> LoadLayers(
        string project,
        bool scoped,
        string normalized,
        string likePattern,
        List<GbCrossPackageBoundary> boundaries)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        var allPkgs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var boundary in boundaries)
        {
            allPkgs.Add(boundary.From);
            allPkgs.Add(boundary.To);
            fanOut.TryGetValue(boundary.From, out var outCount);
            fanOut[boundary.From] = outCount + boundary.CallCount;
            fanIn.TryGetValue(boundary.To, out var inCount);
            fanIn[boundary.To] = inCount + boundary.CallCount;
        }

        var entryPointPackages = CollectEntryPointPackages(project, scoped, normalized, likePattern);
        foreach (var pkg in entryPointPackages)
        {
            allPkgs.Add(pkg);
        }

        return allPkgs
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                var inDeg = fanIn.GetValueOrDefault(name);
                var outDeg = fanOut.GetValueOrDefault(name);
                var hasEntry = entryPointPackages.Contains(name);
                ClassifyLayer(inDeg, outDeg, hasEntry, out var layer, out var reason);
                return new GbPackageLayer(name, layer, reason);
            })
            .ToList();
    }

    private HashSet<string> CollectEntryPointPackages(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT qualified_name
            FROM nodes
            WHERE project = $project
              AND json_extract(properties, '$.is_entry_point') = 1
            """;
        var sql = scoped ? baseSql + GbArchitecturePath.ScopeSql : baseSql;

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var pkgs = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var pkg = GbQualifiedNameHelpers.QnToPackage(GetString(reader, 0));
            if (!string.IsNullOrEmpty(pkg))
            {
                pkgs.Add(pkg);
            }
        }

        return pkgs;
    }

    private static void ClassifyLayer(
        int inDegree,
        int outDegree,
        bool hasEntryPoints,
        out string layer,
        out string reason)
    {
        if (hasEntryPoints && outDegree > 0 && inDegree == 0)
        {
            layer = "entry";
            reason = "has entry points, only outbound calls";
            return;
        }

        if (inDegree > outDegree && inDegree > MinIndegreeForCore)
        {
            layer = "core";
            reason = $"high fan-in ({inDegree} in, {outDegree} out)";
            return;
        }

        if (outDegree == 0 && inDegree > 0)
        {
            layer = "leaf";
            reason = "only inbound calls, no outbound";
            return;
        }

        if (inDegree == 0 && outDegree > 0)
        {
            layer = "entry";
            reason = "only outbound calls";
            return;
        }

        layer = "internal";
        reason = $"fan-in={inDegree}, fan-out={outDegree}";
    }

    private IReadOnlyList<GbClusterInfo> LoadClusters(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var labelList = string.Join(", ", ArchitectureNodeLabels.Select(l => $"'{l}'"));
        var baseSql = $"""
            SELECT id, name, qualified_name
            FROM nodes
            WHERE project = $project AND label IN ({labelList})
            """;
        string sql;
        if (scoped)
        {
            sql = baseSql + GbArchitecturePath.ScopeSql + " ORDER BY id LIMIT $nodeCap";
        }
        else
        {
            sql = baseSql + " ORDER BY id LIMIT $nodeCap";
        }

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        Add(command, "$nodeCap", ClusterNodeCap);

        var ids = new List<long>();
        var names = new List<string>();
        var qns = new List<string>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
                names.Add(GetString(reader, 1));
                qns.Add(GetString(reader, 2));
            }
        }

        if (ids.Count < ClusterMinMembers)
        {
            return Array.Empty<GbClusterInfo>();
        }

        var indexById = new Dictionary<long, int>();
        for (var i = 0; i < ids.Count; i++)
        {
            indexById[ids[i]] = i;
        }

        var edgeList = new List<(long Src, long Dst)>();
        var degree = new int[ids.Count];

        using (var edgeCommand = CreateCommand(
                   """
                   SELECT source_id, target_id
                   FROM edges
                   WHERE project = $project AND type = 'CALLS';
                   """))
        {
            Add(edgeCommand, "$project", project);
            using var reader = edgeCommand.ExecuteReader();
            while (reader.Read())
            {
                var srcId = reader.GetInt64(0);
                var tgtId = reader.GetInt64(1);
                if (!indexById.TryGetValue(srcId, out var si)
                    || !indexById.TryGetValue(tgtId, out var ti)
                    || si == ti)
                {
                    continue;
                }

                edgeList.Add((srcId, tgtId));
                degree[si]++;
                degree[ti]++;
            }
        }

        var idArray = ids.ToArray();
        var communities = LeidenClustering.DetectCommunities(idArray, edgeList.ToArray());

        if (communities.Length != ids.Count)
        {
            return Array.Empty<GbClusterInfo>();
        }

        var commByIndex = communities;

        var communityIds = commByIndex.Distinct().ToList();
        var members = new Dictionary<int, int>();
        var internalEdges = new Dictionary<int, int>();
        var boundaryEdges = new Dictionary<int, int>();

        foreach (var c in communityIds)
        {
            members[c] = 0;
            internalEdges[c] = 0;
            boundaryEdges[c] = 0;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            members[commByIndex[i]]++;
        }

        foreach (var (srcId, tgtId) in edgeList)
        {
            var si = indexById[srcId];
            var ti = indexById[tgtId];
            var cs = commByIndex[si];
            var cd = commByIndex[ti];
            if (cs == cd)
            {
                internalEdges[cs]++;
            }
            else
            {
                boundaryEdges[cs]++;
                boundaryEdges[cd]++;
            }
        }

        return communityIds
            .Select(c => new { Community = c, MemberCount = members[c] })
            .Where(x => x.MemberCount >= ClusterMinMembers)
            .OrderByDescending(x => x.MemberCount)
            .Take(ClusterTopN)
            .Select(x =>
            {
                var c = x.Community;
                var denom = internalEdges[c] + boundaryEdges[c];
                var cohesion = denom > 0 ? (double)internalEdges[c] / denom : 0.0;
                return BuildClusterInfo(c, commByIndex, degree, names, qns, x.MemberCount, cohesion);
            })
            .ToList();
    }

    private static GbClusterInfo BuildClusterInfo(
        int community,
        int[] comm,
        int[] degree,
        List<string> names,
        List<string> qns,
        int members,
        double cohesion)
    {
        var topNodes = new List<(int Index, int Degree)>();
        for (var i = 0; i < comm.Length; i++)
        {
            if (comm[i] != community)
            {
                continue;
            }

            topNodes.Add((i, degree[i]));
        }

        var topNodeNames = topNodes
            .OrderByDescending(x => x.Degree)
            .Take(ClusterMaxTopNodes)
            .Select(x => names[x.Index])
            .ToList();

        var pkgCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < comm.Length; i++)
        {
            if (comm[i] != community)
            {
                continue;
            }

            var pkg = GbQualifiedNameHelpers.QnToTopPackage(qns[i]);
            if (string.IsNullOrEmpty(pkg))
            {
                continue;
            }

            pkgCounts.TryGetValue(pkg, out var count);
            pkgCounts[pkg] = count + 1;
        }

        var packages = pkgCounts
            .OrderByDescending(pair => pair.Value)
            .Take(ClusterMaxPkgs)
            .Select(pair => pair.Key)
            .ToList();

        var label = packages.Count > 0
            ? packages[0]
            : topNodeNames.Count > 0 ? topNodeNames[0] : "cluster";

        return new GbClusterInfo(
            Id: community,
            Label: label,
            Members: members,
            Cohesion: cohesion,
            TopNodes: topNodeNames,
            Packages: packages,
            EdgeTypes: ["CALLS"]);
    }

    private IReadOnlyList<GbFileTreeEntry> LoadFileTree(
        string project,
        bool scoped,
        string normalized,
        string likePattern)
    {
        var baseSql = """
            SELECT file_path
            FROM nodes
            WHERE project = $project AND label = 'File'
            """;
        var sql = scoped ? baseSql + GbArchitecturePath.ScopeSql : baseSql;

        using var command = CreateCommand(sql);
        Add(command, "$project", project);
        if (scoped)
        {
            BindPathScope(command, normalized, likePattern);
        }

        var files = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var path = GetString(reader, 0);
                if (!string.IsNullOrEmpty(path))
                {
                    files.Add(path);
                }
            }
        }

        if (files.Count == 0)
        {
            return Array.Empty<GbFileTreeEntry>();
        }

        var childCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var children = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            RegisterFileTreePath(file, files, childCounts, children);
        }

        var entries = new List<GbFileTreeEntry>();

        if (children.TryGetValue(string.Empty, out var rootChildren))
        {
            foreach (var child in rootChildren.OrderBy(x => x, StringComparer.Ordinal))
            {
                entries.Add(MakeTreeEntry(child, files, childCounts));
            }
        }

        foreach (var dir in childCounts.Keys.Where(k => k.Contains('/', StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
        {
            entries.Add(MakeTreeEntry(dir, files, childCounts));
        }

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            entries.Add(new GbFileTreeEntry(file, "file", 0));
        }

        return entries;
    }

    private static void RegisterFileTreePath(
        string filePath,
        HashSet<string> files,
        Dictionary<string, int> childCounts,
        Dictionary<string, HashSet<string>> children)
    {
        var parts = filePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        AddTreeChild(string.Empty, parts[0], childCounts, children);

        for (var depth = 0; depth < parts.Length - 1; depth++)
        {
            var dir = string.Join('/', parts.Take(depth + 1));
            var child = parts[depth + 1];
            AddTreeChild(dir, child, childCounts, children);
        }
    }

    private static void AddTreeChild(
        string dir,
        string child,
        Dictionary<string, int> childCounts,
        Dictionary<string, HashSet<string>> children)
    {
        if (!children.TryGetValue(dir, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            children[dir] = set;
            childCounts.TryAdd(dir, 0);
        }

        if (set.Add(child))
        {
            childCounts[dir] = set.Count;
        }
    }

    private static GbFileTreeEntry MakeTreeEntry(
        string path,
        HashSet<string> files,
        Dictionary<string, int> childCounts)
    {
        var type = files.Contains(path) ? "file" : "dir";
        var children = childCounts.GetValueOrDefault(path);
        return new GbFileTreeEntry(path, type, children);
    }
}
