using System.Globalization;
using System.Text.RegularExpressions;
using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore : IDisposable
{
    private const string DefaultPropertiesJson = "{}";
    private const string CallsEdgeType = "CALLS";
    private const long DefaultMmapSize = 67_108_864;

    private readonly SqliteConnection connection;
    private readonly bool inMemory;

    private CbmStore(SqliteConnection connection, bool inMemory)
    {
        this.connection = connection;
        this.inMemory = inMemory;
        RegisterFunctions();
        ConfigurePragmas();
        InitializeSchema();
        CreateIndexes();
    }

    public static CbmStore OpenMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return new CbmStore(connection, inMemory: true);
    }

    public static CbmStore OpenPath(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return new CbmStore(connection, inMemory: false);
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    public void BeginBulk()
    {
        ExecuteNonQuery("PRAGMA synchronous = OFF;");
        ExecuteNonQuery("PRAGMA cache_size = -65536;");
    }

    public void EndBulk()
    {
        ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
        ExecuteNonQuery("PRAGMA cache_size = -2000;");
    }

    public void DumpToFile(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = tempPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        using (var destination = new SqliteConnection(builder.ToString()))
        {
            destination.Open();
            connection.BackupDatabase(destination);

            using var command = destination.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    public void UpsertProject(string name, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var command = CreateCommand(
            """
            INSERT INTO projects (name, indexed_at, root_path)
            VALUES ($name, $indexedAt, $rootPath)
            ON CONFLICT(name) DO UPDATE SET
                indexed_at = $indexedAt,
                root_path = $rootPath;
            """);
        Add(command, "$name", name);
        Add(command, "$indexedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$rootPath", rootPath);
        command.ExecuteNonQuery();
    }

    public void UpsertFileHashBatch(IEnumerable<CbmFileHash> fileHashes)
    {
        ArgumentNullException.ThrowIfNull(fileHashes);

        using var transaction = connection.BeginTransaction();
        foreach (var fileHash in fileHashes)
        {
            using var command = CreateCommand(
                """
                INSERT INTO file_hashes (project, rel_path, sha256, mtime_ns, size)
                VALUES ($project, $relPath, $sha256, $mtimeNs, $size)
                ON CONFLICT(project, rel_path) DO UPDATE SET
                    sha256 = $sha256,
                    mtime_ns = $mtimeNs,
                    size = $size;
                """,
                transaction);
            Add(command, "$project", fileHash.Project);
            Add(command, "$relPath", fileHash.RelativePath);
            Add(command, "$sha256", fileHash.Sha256);
            Add(command, "$mtimeNs", fileHash.MtimeNs);
            Add(command, "$size", fileHash.Size);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public long UpsertNode(CbmNode node)
    {
        var id = UpsertNode(node, transaction: null);
        RebuildFtsIndex();
        return id;
    }

    public IReadOnlyList<long> UpsertNodeBatch(IEnumerable<CbmNode> nodes, bool rebuildFts = true)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var ids = new List<long>();
        using var transaction = connection.BeginTransaction();
        foreach (var node in nodes)
        {
            ids.Add(UpsertNode(node, transaction));
        }

        transaction.Commit();
        if (rebuildFts)
        {
            RebuildFtsIndex();
        }

        return ids;
    }

    public void RefreshFtsIndex()
    {
        RebuildFtsIndex();
    }

    public long UpsertEdge(CbmEdge edge)
    {
        return UpsertEdge(edge, transaction: null);
    }

    public void UpsertEdgeBatch(IEnumerable<CbmEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        using var transaction = connection.BeginTransaction();
        foreach (var edge in edges)
        {
            UpsertEdge(edge, transaction);
        }

        transaction.Commit();
    }

    public CbmNode? FindNodeById(long id)
    {
        using var command = CreateCommand(
            """
            SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
            FROM nodes
            WHERE id = $id;
            """);
        Add(command, "$id", id);
        return ReadSingleNode(command);
    }

    public CbmNode? FindNodeByQualifiedName(string project, string qualifiedName)
    {
        using var command = CreateCommand(
            """
            SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
            FROM nodes
            WHERE project = $project AND qualified_name = $qualifiedName;
            """);
        Add(command, "$project", project);
        Add(command, "$qualifiedName", qualifiedName);
        return ReadSingleNode(command);
    }

    public IReadOnlyList<CbmNode> FindNodesByQualifiedNameSuffix(string project, string suffix)
    {
        using var command = CreateCommand(
            """
            SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
            FROM nodes
            WHERE project = $project
              AND (qualified_name LIKE $likePattern OR qualified_name = $suffix)
            ORDER BY qualified_name;
            """);
        Add(command, "$project", project);
        Add(command, "$likePattern", "%." + suffix);
        Add(command, "$suffix", suffix);
        return ReadNodes(command);
    }

    public IReadOnlyList<CbmNode> FindNodesByFile(string project, string filePath)
    {
        using var command = CreateCommand(
            """
            SELECT id, project, label, name, qualified_name, file_path, start_line, end_line, properties
            FROM nodes
            WHERE project = $project AND file_path = $filePath;
            """);
        Add(command, "$project", project);
        Add(command, "$filePath", filePath);
        return ReadNodes(command);
    }

    public CbmNodeDegree GetNodeDegree(long nodeId)
    {
        var inDegree = CountEdges("target_id", nodeId, CallsEdgeType);
        var outDegree = CountEdges("source_id", nodeId, CallsEdgeType);
        return new CbmNodeDegree(inDegree, outDegree);
    }

    public IReadOnlyDictionary<long, CbmNodeDegree> BatchCountDegrees(
        IReadOnlyCollection<long> nodeIds,
        string? edgeType = null)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        var result = nodeIds.Distinct().ToDictionary(id => id, _ => new CbmNodeDegree(0, 0));
        if (result.Count == 0)
        {
            return result;
        }

        var placeholders = string.Join(", ", result.Keys.Select((_, index) => "$id" + index));
        var edgeFilter = string.IsNullOrWhiteSpace(edgeType) ? string.Empty : " AND type = $edgeType";

        AddGroupedCounts(
            result,
            $"SELECT target_id, COUNT(*) FROM edges WHERE target_id IN ({placeholders}){edgeFilter} GROUP BY target_id;",
            isInbound: true,
            edgeType);
        AddGroupedCounts(
            result,
            $"SELECT source_id, COUNT(*) FROM edges WHERE source_id IN ({placeholders}){edgeFilter} GROUP BY source_id;",
            isInbound: false,
            edgeType);

        return result;
    }

    public CbmNodeNeighbors GetNodeNeighborNames(long nodeId, int limit)
    {
        var normalizedLimit = limit <= 0 ? 10 : limit;
        var callers = QueryNeighborNames(
            """
            SELECT DISTINCT n.name
            FROM edges e
            JOIN nodes n ON e.source_id = n.id
            WHERE e.target_id = $nodeId
              AND e.type IN ('CALLS', 'HTTP_CALLS', 'ASYNC_CALLS')
            ORDER BY n.name
            LIMIT $limit;
            """,
            nodeId,
            normalizedLimit);
        var callees = QueryNeighborNames(
            """
            SELECT DISTINCT n.name
            FROM edges e
            JOIN nodes n ON e.target_id = n.id
            WHERE e.source_id = $nodeId
              AND e.type IN ('CALLS', 'HTTP_CALLS', 'ASYNC_CALLS')
            ORDER BY n.name
            LIMIT $limit;
            """,
            nodeId,
            normalizedLimit);

        return new CbmNodeNeighbors(callers, callees);
    }

    public IReadOnlyList<string> ListFiles(string project)
    {
        using var command = CreateCommand(
            """
            SELECT DISTINCT file_path
            FROM nodes
            WHERE project = $project
              AND file_path IS NOT NULL
              AND file_path != ''
            ORDER BY file_path;
            """);
        Add(command, "$project", project);

        var files = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }

    public IReadOnlyList<CbmNode> SearchNodes(
        string project,
        string? query = null,
        string? label = null,
        string? namePattern = null,
        string? qualifiedNamePattern = null,
        string? filePattern = null,
        bool caseSensitive = false,
        int limit = 10,
        int offset = 0)
    {
        var (sql, parameters) = BuildSearchSql(
            project,
            query,
            label,
            namePattern,
            qualifiedNamePattern,
            filePattern,
            caseSensitive,
            limit,
            offset,
            selectOnly: false);

        using var command = CreateCommand(sql);
        BindSearchParameters(command, parameters);
        return ReadNodes(command);
    }

    public int CountSearchNodes(
        string project,
        string? query = null,
        string? label = null,
        string? namePattern = null,
        string? qualifiedNamePattern = null,
        string? filePattern = null,
        bool caseSensitive = false)
    {
        var (sql, parameters) = BuildSearchSql(
            project,
            query,
            label,
            namePattern,
            qualifiedNamePattern,
            filePattern,
            caseSensitive,
            limit: null,
            offset: null,
            selectOnly: true);

        using var command = CreateCommand(sql);
        BindSearchParameters(command, parameters);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static (string Sql, SearchParameters Parameters) BuildSearchSql(
        string project,
        string? query,
        string? label,
        string? namePattern,
        string? qualifiedNamePattern,
        string? filePattern,
        bool caseSensitive,
        int? limit,
        int? offset,
        bool selectOnly)
    {
        var whereClauses = new List<string> { "n.project = $project" };
        if (!string.IsNullOrWhiteSpace(label))
        {
            whereClauses.Add("n.label = $label");
        }

        var regexFunction = caseSensitive ? "regexp" : "iregexp";
        if (!string.IsNullOrWhiteSpace(namePattern))
        {
            whereClauses.Add($"{regexFunction}($namePattern, n.name) = 1");
        }

        if (!string.IsNullOrWhiteSpace(qualifiedNamePattern))
        {
            whereClauses.Add($"{regexFunction}($qualifiedNamePattern, n.qualified_name) = 1");
        }

        if (!string.IsNullOrWhiteSpace(filePattern))
        {
            whereClauses.Add($"{regexFunction}($filePattern, n.file_path) = 1");
        }

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var where = string.Join(" AND ", whereClauses);
        string sql;
        if (selectOnly)
        {
            sql = hasQuery
                ? $"""
                   SELECT COUNT(*)
                   FROM nodes_fts f
                   JOIN nodes n ON n.id = f.rowid
                   WHERE nodes_fts MATCH $query
                     AND {where};
                   """
                : $"""
                   SELECT COUNT(*)
                   FROM nodes n
                   WHERE {where};
                   """;
        }
        else
        {
            sql = hasQuery
                ? $"""
                   SELECT n.id, n.project, n.label, n.name, n.qualified_name, n.file_path,
                          n.start_line, n.end_line, n.properties
                   FROM nodes_fts f
                   JOIN nodes n ON n.id = f.rowid
                   WHERE nodes_fts MATCH $query
                     AND {where}
                   ORDER BY bm25(nodes_fts), n.qualified_name
                   LIMIT $limit OFFSET $offset;
                   """
                : $"""
                   SELECT n.id, n.project, n.label, n.name, n.qualified_name, n.file_path,
                          n.start_line, n.end_line, n.properties
                   FROM nodes n
                   WHERE {where}
                   ORDER BY n.qualified_name
                   LIMIT $limit OFFSET $offset;
                   """;
        }

        return (sql, new SearchParameters(
            project,
            query,
            label,
            namePattern,
            qualifiedNamePattern,
            filePattern,
            limit,
            offset));
    }

    private static void BindSearchParameters(SqliteCommand command, SearchParameters parameters)
    {
        Add(command, "$project", parameters.Project);
        if (!string.IsNullOrWhiteSpace(parameters.Query))
        {
            Add(command, "$query", parameters.Query);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Label))
        {
            Add(command, "$label", parameters.Label);
        }

        if (!string.IsNullOrWhiteSpace(parameters.NamePattern))
        {
            Add(command, "$namePattern", parameters.NamePattern);
        }

        if (!string.IsNullOrWhiteSpace(parameters.QualifiedNamePattern))
        {
            Add(command, "$qualifiedNamePattern", parameters.QualifiedNamePattern);
        }

        if (!string.IsNullOrWhiteSpace(parameters.FilePattern))
        {
            Add(command, "$filePattern", parameters.FilePattern);
        }

        if (parameters.Limit.HasValue)
        {
            Add(command, "$limit", parameters.Limit.Value <= 0 ? 10 : parameters.Limit.Value);
        }

        if (parameters.Offset.HasValue)
        {
            Add(command, "$offset", Math.Max(0, parameters.Offset.Value));
        }
    }

    private sealed record SearchParameters(
        string Project,
        string? Query,
        string? Label,
        string? NamePattern,
        string? QualifiedNamePattern,
        string? FilePattern,
        int? Limit,
        int? Offset);

    public CbmProject? GetProject(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var command = CreateCommand(
            """
            SELECT name, indexed_at, root_path
            FROM projects
            WHERE name = $name;
            """);
        Add(command, "$name", name);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new CbmProject
        {
            Name = reader.GetString(0),
            IndexedAt = reader.GetString(1),
            RootPath = reader.GetString(2),
        };
    }

    public IReadOnlyList<CbmProject> ListProjects()
    {
        using var command = CreateCommand(
            """
            SELECT name, indexed_at, root_path
            FROM projects
            ORDER BY name;
            """);

        var projects = new List<CbmProject>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(new CbmProject
            {
                Name = reader.GetString(0),
                IndexedAt = reader.GetString(1),
                RootPath = reader.GetString(2),
            });
        }

        return projects;
    }

    public bool DeleteProject(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var command = CreateCommand("DELETE FROM projects WHERE name = $name;");
        Add(command, "$name", name);
        return command.ExecuteNonQuery() > 0;
    }

    public int CountEdges(string project)
    {
        using var command = CreateCommand("SELECT COUNT(*) FROM edges WHERE project = $project;");
        Add(command, "$project", project);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public CbmGraphSchema GetSchemaCounts(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var labels = new List<CbmLabelSchema>();
        using (var command = CreateCommand(
                   """
                   SELECT label, COUNT(*)
                   FROM nodes
                   WHERE project = $project
                   GROUP BY label
                   ORDER BY COUNT(*) DESC, label;
                   """))
        {
            Add(command, "$project", project);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                labels.Add(new CbmLabelSchema(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        var edgeTypes = new List<CbmEdgeTypeSchema>();
        using (var command = CreateCommand(
                   """
                   SELECT type, COUNT(*)
                   FROM edges
                   WHERE project = $project
                   GROUP BY type
                   ORDER BY COUNT(*) DESC, type;
                   """))
        {
            Add(command, "$project", project);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                edgeTypes.Add(new CbmEdgeTypeSchema(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        return new CbmGraphSchema(labels, edgeTypes);
    }

    public void UpsertGraphEdges(
        string project,
        IReadOnlyDictionary<string, long> idsByQualifiedName,
        IEnumerable<CbmGraphEdge> graphEdges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(idsByQualifiedName);
        ArgumentNullException.ThrowIfNull(graphEdges);

        var materialized = new List<CbmEdge>();
        foreach (var graphEdge in graphEdges)
        {
            if (!idsByQualifiedName.TryGetValue(graphEdge.SourceQualifiedName, out var sourceId))
            {
                continue;
            }

            if (!idsByQualifiedName.TryGetValue(graphEdge.TargetQualifiedName, out var targetId))
            {
                continue;
            }

            materialized.Add(new CbmEdge
            {
                Project = project,
                SourceId = sourceId,
                TargetId = targetId,
                Type = graphEdge.Type,
                PropertiesJson = graphEdge.PropertiesJson,
            });
        }

        if (materialized.Count > 0)
        {
            UpsertEdgeBatch(materialized);
        }
    }

    public int CountNodes(string project)
    {
        using var command = CreateCommand("SELECT COUNT(*) FROM nodes WHERE project = $project;");
        Add(command, "$project", project);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public const int DefaultQueryMaxRows = 100_000;

    public CbmCypherQueryResult ExecuteQuery(
        string sql,
        IEnumerable<KeyValuePair<string, object?>> parameters,
        int maxRows = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        if (maxRows <= 0)
        {
            maxRows = DefaultQueryMaxRows;
        }

        using var command = CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            Add(command, parameter.Key, parameter.Value);
        }

        using var reader = command.ExecuteReader();
        var columns = new List<string>(reader.FieldCount);
        for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
        {
            columns.Add(reader.GetName(columnIndex));
        }

        var rows = new List<IReadOnlyList<string>>();
        while (reader.Read())
        {
            if (rows.Count >= maxRows)
            {
                throw new InvalidOperationException(
                    "result exceeded 100k rows — use narrower filters or add LIMIT");
            }

            var row = new string[reader.FieldCount];
            for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
            {
                row[columnIndex] = reader.IsDBNull(columnIndex)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(columnIndex), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(row);
        }

        return new CbmCypherQueryResult(columns, rows);
    }

    private long UpsertNode(CbmNode node, SqliteTransaction? transaction)
    {
        using var command = CreateCommand(
            """
            INSERT INTO nodes (project, label, name, qualified_name, file_path, start_line, end_line, properties)
            VALUES ($project, $label, $name, $qualifiedName, $filePath, $startLine, $endLine, $properties)
            ON CONFLICT(project, qualified_name) DO UPDATE SET
                label = $label,
                name = $name,
                file_path = $filePath,
                start_line = $startLine,
                end_line = $endLine,
                properties = $properties
            RETURNING id;
            """,
            transaction);
        Add(command, "$project", node.Project);
        Add(command, "$label", node.Label);
        Add(command, "$name", node.Name);
        Add(command, "$qualifiedName", node.QualifiedName);
        Add(command, "$filePath", node.FilePath);
        Add(command, "$startLine", node.StartLine);
        Add(command, "$endLine", node.EndLine);
        Add(command, "$properties", SafeProperties(node.PropertiesJson));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private long UpsertEdge(CbmEdge edge, SqliteTransaction? transaction)
    {
        using var command = CreateCommand(
            """
            INSERT INTO edges (project, source_id, target_id, type, properties)
            VALUES ($project, $sourceId, $targetId, $type, $properties)
            ON CONFLICT(source_id, target_id, type) DO UPDATE SET
                project = $project,
                properties = $properties
            RETURNING id;
            """,
            transaction);
        Add(command, "$project", edge.Project);
        Add(command, "$sourceId", edge.SourceId);
        Add(command, "$targetId", edge.TargetId);
        Add(command, "$type", edge.Type);
        Add(command, "$properties", SafeProperties(edge.PropertiesJson));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void RegisterFunctions()
    {
        connection.CreateFunction<string?, string?, int>(
            "regexp",
            (pattern, text) => RegexMatches(pattern, text, ignoreCase: false),
            isDeterministic: true);
        connection.CreateFunction<string?, string?, int>(
            "iregexp",
            (pattern, text) => RegexMatches(pattern, text, ignoreCase: true),
            isDeterministic: true);
        connection.CreateFunction<string?, string>(
            "cbm_camel_split",
            CamelSplit,
            isDeterministic: true);
    }

    private void ConfigurePragmas()
    {
        ExecuteNonQuery("PRAGMA foreign_keys = ON;");
        ExecuteNonQuery("PRAGMA temp_store = MEMORY;");

        if (inMemory)
        {
            ExecuteNonQuery("PRAGMA synchronous = OFF;");
            return;
        }

        ExecuteNonQuery("PRAGMA busy_timeout = 10000;");
        ExecuteNonQuery("PRAGMA journal_mode = WAL;");
        ExecuteNonQuery("PRAGMA wal_checkpoint(PASSIVE);");
        ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
        ExecuteNonQuery("PRAGMA mmap_size = " + ResolveMmapSize().ToString(CultureInfo.InvariantCulture) + ";");
    }

    private void InitializeSchema()
    {
        ExecuteNonQuery(
            """
            CREATE TABLE IF NOT EXISTS projects (
              name TEXT PRIMARY KEY,
              indexed_at TEXT NOT NULL,
              root_path TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS file_hashes (
              project TEXT NOT NULL REFERENCES projects(name) ON DELETE CASCADE,
              rel_path TEXT NOT NULL,
              sha256 TEXT NOT NULL,
              mtime_ns INTEGER NOT NULL DEFAULT 0,
              size INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY (project, rel_path)
            );
            CREATE TABLE IF NOT EXISTS nodes (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              project TEXT NOT NULL REFERENCES projects(name) ON DELETE CASCADE,
              label TEXT NOT NULL,
              name TEXT NOT NULL,
              qualified_name TEXT NOT NULL,
              file_path TEXT DEFAULT '',
              start_line INTEGER DEFAULT 0,
              end_line INTEGER DEFAULT 0,
              properties TEXT DEFAULT '{}',
              UNIQUE(project, qualified_name)
            );
            CREATE TABLE IF NOT EXISTS edges (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              project TEXT NOT NULL REFERENCES projects(name) ON DELETE CASCADE,
              source_id INTEGER NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
              target_id INTEGER NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
              type TEXT NOT NULL,
              properties TEXT DEFAULT '{}',
              url_path_gen TEXT GENERATED ALWAYS AS (json_extract(properties,'$.url_path')),
              UNIQUE(source_id, target_id, type)
            );
            CREATE TABLE IF NOT EXISTS project_summaries (
              project TEXT PRIMARY KEY,
              summary TEXT NOT NULL,
              source_hash TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS trace_observations (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              project TEXT NOT NULL REFERENCES projects(name) ON DELETE CASCADE,
              caller TEXT NOT NULL DEFAULT '',
              callee TEXT NOT NULL DEFAULT '',
              service TEXT NOT NULL DEFAULT '',
              target_service TEXT NOT NULL DEFAULT '',
              route TEXT NOT NULL DEFAULT '',
              method TEXT NOT NULL DEFAULT '',
              caller_node_id INTEGER REFERENCES nodes(id) ON DELETE SET NULL,
              callee_node_id INTEGER REFERENCES nodes(id) ON DELETE SET NULL,
              calls_edge_id INTEGER REFERENCES edges(id) ON DELETE SET NULL,
              count INTEGER NOT NULL DEFAULT 0,
              error_count INTEGER NOT NULL DEFAULT 0,
              avg_duration_ms REAL NOT NULL DEFAULT 0,
              p99_duration_ms REAL NOT NULL DEFAULT 0,
              last_seen TEXT NOT NULL,
              duration_samples TEXT NOT NULL DEFAULT '[]',
              attributes TEXT NOT NULL DEFAULT '{}',
              UNIQUE(project, caller, callee, service, target_service, route, method)
            );
            """);

        ExecuteNonQuery(
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS nodes_fts USING fts5(
              name, qualified_name, label, file_path,
              content='',
              tokenize='unicode61 remove_diacritics 2'
            );
            """);
    }

    private void CreateIndexes()
    {
        ExecuteNonQuery(
            """
            CREATE INDEX IF NOT EXISTS idx_nodes_label ON nodes(project, label);
            CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(project, name);
            CREATE INDEX IF NOT EXISTS idx_nodes_file ON nodes(project, file_path);
            CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id, type);
            CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id, type);
            CREATE INDEX IF NOT EXISTS idx_edges_type ON edges(project, type);
            CREATE INDEX IF NOT EXISTS idx_edges_target_type ON edges(project, target_id, type);
            CREATE INDEX IF NOT EXISTS idx_edges_source_type ON edges(project, source_id, type);
            CREATE INDEX IF NOT EXISTS idx_edges_url_path ON edges(project, url_path_gen);
            CREATE INDEX IF NOT EXISTS idx_trace_obs_project ON trace_observations(project);
            CREATE INDEX IF NOT EXISTS idx_trace_obs_count ON trace_observations(project, count DESC);
            """);
    }

    private void RebuildFtsIndex()
    {
        ExecuteNonQuery("INSERT INTO nodes_fts(nodes_fts) VALUES('delete-all');");
        ExecuteNonQuery(
            """
            INSERT INTO nodes_fts(rowid, name, qualified_name, label, file_path)
            SELECT id, cbm_camel_split(name), cbm_camel_split(qualified_name), label, file_path
            FROM nodes;
            """);
    }

    private int CountEdges(string columnName, long nodeId, string edgeType)
    {
        using var command = CreateCommand($"SELECT COUNT(*) FROM edges WHERE {columnName} = $nodeId AND type = $edgeType;");
        Add(command, "$nodeId", nodeId);
        Add(command, "$edgeType", edgeType);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void AddGroupedCounts(
        Dictionary<long, CbmNodeDegree> result,
        string sql,
        bool isInbound,
        string? edgeType)
    {
        using var command = CreateCommand(sql);
        var index = 0;
        foreach (var nodeId in result.Keys)
        {
            Add(command, "$id" + index, nodeId);
            index++;
        }

        if (!string.IsNullOrWhiteSpace(edgeType))
        {
            Add(command, "$edgeType", edgeType);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var nodeId = reader.GetInt64(0);
            var count = reader.GetInt32(1);
            var current = result[nodeId];
            result[nodeId] = isInbound
                ? current with { InDegree = count }
                : current with { OutDegree = count };
        }
    }

    private IReadOnlyList<string> QueryNeighborNames(string sql, long nodeId, int limit)
    {
        using var command = CreateCommand(sql);
        Add(command, "$nodeId", nodeId);
        Add(command, "$limit", limit);

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private CbmNode? ReadSingleNode(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadNode(reader) : null;
    }

    private static IReadOnlyList<CbmNode> ReadNodes(SqliteCommand command)
    {
        var nodes = new List<CbmNode>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    private static CbmNode ReadNode(SqliteDataReader reader)
    {
        return new CbmNode
        {
            Id = reader.GetInt64(0),
            Project = GetString(reader, 1),
            Label = GetString(reader, 2),
            Name = GetString(reader, 3),
            QualifiedName = GetString(reader, 4),
            FilePath = GetString(reader, 5),
            StartLine = reader.GetInt32(6),
            EndLine = reader.GetInt32(7),
            PropertiesJson = SafeProperties(GetString(reader, 8)),
        };
    }

    private SqliteCommand CreateCommand(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    private void ExecuteNonQuery(string sql)
    {
        using var command = CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string GetString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static string SafeProperties(string? propertiesJson)
    {
        return string.IsNullOrWhiteSpace(propertiesJson) ? DefaultPropertiesJson : propertiesJson;
    }

    private static int RegexMatches(string? pattern, string? text, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(pattern) || text is null)
        {
            return 0;
        }

        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.IsMatch(text, pattern, options) ? 1 : 0;
    }

    private static string CamelSplit(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var parts = new List<char>(input.Length * 2) { };
        parts.AddRange(input);
        parts.Add(' ');

        for (var i = 0; i < input.Length; i++)
        {
            if (ShouldSplitCamel(input, i))
            {
                parts.Add(' ');
            }

            parts.Add(input[i]);
        }

        return new string(parts.ToArray());
    }

    private static bool ShouldSplitCamel(string input, int index)
    {
        if (index <= 0)
        {
            return false;
        }

        var current = input[index];
        var previous = input[index - 1];
        var next = index + 1 < input.Length ? input[index + 1] : '\0';

        return char.IsAsciiLetterUpper(current)
            && (char.IsAsciiLetterLower(previous)
                || (char.IsAsciiLetterUpper(previous) && char.IsAsciiLetterLower(next)));
    }

    private static long ResolveMmapSize()
    {
        var rawValue = Environment.GetEnvironmentVariable("CBM_SQLITE_MMAP_SIZE");
        if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return DefaultMmapSize;
        }

        return parsed < 0 ? 0 : parsed;
    }
}
