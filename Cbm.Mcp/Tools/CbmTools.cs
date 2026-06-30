using System.ComponentModel;
using System.Text.Json;
using Cbm.Cypher;
using Cbm.Mcp;
using Cbm.Pipeline;
using Cbm.Store;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Cbm.Mcp.Tools;

[McpServerToolType]
public sealed class CbmTools
{
    private readonly IndexRepository indexRepository;
    private readonly SearchGraphService searchGraphService;
    private readonly CodeSnippetService codeSnippetService;
    private readonly GraphSchemaService graphSchemaService;
    private readonly QueryGraphService queryGraphService;
    private readonly GraphArchitectureService graphArchitectureService;
    private readonly TracePathService tracePathService;
    private readonly SearchCodeService searchCodeService;
    private readonly ManageAdrService manageAdrService;
    private readonly IngestTracesService ingestTracesService;
    private readonly DetectChangesService detectChangesService;

    public CbmTools(
        IndexRepository indexRepository,
        SearchGraphService searchGraphService,
        CodeSnippetService codeSnippetService,
        GraphSchemaService graphSchemaService,
        QueryGraphService queryGraphService,
        GraphArchitectureService graphArchitectureService,
        TracePathService tracePathService,
        SearchCodeService searchCodeService,
        ManageAdrService manageAdrService,
        IngestTracesService ingestTracesService,
        DetectChangesService detectChangesService)
    {
        this.indexRepository = indexRepository;
        this.searchGraphService = searchGraphService;
        this.codeSnippetService = codeSnippetService;
        this.graphSchemaService = graphSchemaService;
        this.queryGraphService = queryGraphService;
        this.graphArchitectureService = graphArchitectureService;
        this.tracePathService = tracePathService;
        this.searchCodeService = searchCodeService;
        this.manageAdrService = manageAdrService;
        this.ingestTracesService = ingestTracesService;
        this.detectChangesService = detectChangesService;
    }

    [McpServerTool(Name = "list_tools")]
    [Description("Return documentation for all CBM MCP tools.")]
    public Task<string> ListToolsAsync(
        [Description("Output format: json (default) or markdown.")] string? format = null,
        [Description("Optional exact tool name filter; omit for all tools.")] string? name = null,
        [Description("Optional exact tool names filter; use this to request docs for multiple relevant tools.")] string[]? tools = null,
        [Description("Optional category filter: lifecycle, query, mutation, or meta.")] string? category = null)
    {
        var selectedTools = CbmToolCatalog.Tools;
        var requestedToolNames = BuildRequestedToolNames(name, tools);
        if (requestedToolNames.Count > 0)
        {
            var matchedTools = new List<CbmToolDefinition>();
            var missingToolNames = new List<string>();
            foreach (var toolName in requestedToolNames)
            {
                var tool = CbmToolCatalog.FindByName(toolName);
                if (tool is null)
                {
                    missingToolNames.Add(toolName);
                    continue;
                }

                matchedTools.Add(tool);
            }

            if (missingToolNames.Count > 0)
            {
                throw new McpException(JsonSerializer.Serialize(
                    new
                    {
                        error = "tool not found",
                        missing_tools = missingToolNames,
                        available_tools = CbmToolCatalog.Tools.Select(tool => tool.Name).ToArray(),
                        hint = "Call list_tools without filters to see available tools.",
                    },
                    CbmMcpJson.Options));
            }

            selectedTools = matchedTools;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            selectedTools = selectedTools
                .Where(tool => string.Equals(tool.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (selectedTools.Count == 0)
            {
                throw new McpException(JsonSerializer.Serialize(
                    new
                    {
                        error = "no tools matched",
                        category = normalizedCategory,
                        available_categories = CbmToolCatalog.Categories,
                        hint = "Use category=lifecycle, query, mutation, or meta, or omit category.",
                    },
                    CbmMcpJson.Options));
            }
        }

        var normalizedFormat = string.IsNullOrWhiteSpace(format)
            ? "json"
            : format.Trim().ToLowerInvariant();

        return normalizedFormat switch
        {
            "json" => Task.FromResult(CbmMcpJson.FormatListTools(selectedTools)),
            "markdown" => Task.FromResult(CbmToolCatalog.RenderMarkdown(selectedTools)),
            _ => throw new McpException(JsonSerializer.Serialize(
                new
                {
                    error = "invalid format",
                    hint = "Use format=json or format=markdown.",
                },
                CbmMcpJson.Options)),
        };
    }

    private static IReadOnlyList<string> BuildRequestedToolNames(string? name, string[]? tools)
    {
        var requestedToolNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            requestedToolNames.Add(name.Trim());
        }

        if (tools is not null)
        {
            foreach (var toolName in tools)
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    requestedToolNames.Add(toolName.Trim());
                }
            }
        }

        return requestedToolNames.Distinct(StringComparer.Ordinal).ToArray();
    }

    [McpServerTool(Name = "index_repository")]
    [Description("Index a C# repository into the knowledge graph.")]
    public async Task<string> IndexRepositoryAsync(
        [Description("Path to the repository root, solution, or project file.")] string repo_path,
        [Description("Indexing mode. Only full reindex is supported in this build.")] string? mode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo_path))
        {
            throw new McpException("repo_path is required");
        }

        if (!string.IsNullOrWhiteSpace(mode)
            && string.Equals(mode, "cross-repo-intelligence", StringComparison.Ordinal))
        {
            throw new McpException("mode 'cross-repo-intelligence' is not supported in the C# port yet");
        }

        var result = await indexRepository.IndexAsync(repo_path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return CbmMcpJson.FormatIndexResult(result);
    }

    [McpServerTool(Name = "index_status")]
    [Description("Return node/edge counts and status for an indexed project.")]
    public Task<string> IndexStatusAsync(
        [Description("Indexed project name.")] string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        EnsureProjectDatabaseExists(project);
        return Task.FromResult(CbmMcpJson.FormatIndexStatus(CbmProjectCatalog.GetIndexStatus(project)));
    }

    [McpServerTool(Name = "list_projects")]
    [Description("List indexed projects in the local CBM cache.")]
    public Task<string> ListProjectsAsync()
    {
        return Task.FromResult(CbmMcpJson.FormatListProjects(CbmProjectCatalog.ListProjects()));
    }

    [McpServerTool(Name = "delete_project")]
    [Description("Delete a project's cached index database.")]
    public Task<string> DeleteProjectAsync(
        [Description("Indexed project name.")] string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        var deleted = CbmProjectCatalog.DeleteProject(project);
        if (!deleted)
        {
            throw new McpException(CbmMcpJson.FormatDeleteProject(project, deleted: false));
        }

        return Task.FromResult(CbmMcpJson.FormatDeleteProject(project, deleted: true));
    }

    [McpServerTool(Name = "search_graph")]
    [Description("Search the code knowledge graph using BM25 query and/or regex filters.")]
    public Task<string> SearchGraphAsync(
        [Description("Indexed project name.")] string project,
        [Description("BM25 full-text query.")] string? query = null,
        [Description("Node label filter, e.g. Method or Class.")] string? label = null,
        [Description("Regex matched against node name.")] string? name_pattern = null,
        [Description("Regex matched against qualified_name.")] string? qn_pattern = null,
        [Description("Regex matched against file_path.")] string? file_pattern = null,
        [Description("When true, regex matching is case-sensitive.")] bool case_sensitive = false,
        [Description("Maximum results to return.")] int limit = 10,
        [Description("Number of matching nodes to skip before returning results.")] int offset = 0,
        string[]? semantic_query = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (semantic_query is { Length: > 0 })
        {
            throw new McpException(
                "semantic_query is not supported in the C# port (no embeddings). "
                + "Use query= for BM25 search or name_pattern/qn_pattern for regex search.");
        }

        EnsureProjectIndexed(project);

        var search = searchGraphService.Search(
            project,
            query,
            label,
            name_pattern,
            qn_pattern,
            file_pattern,
            case_sensitive,
            limit,
            offset);

        var searchMode = !string.IsNullOrWhiteSpace(query) ? "bm25" : null;
        return Task.FromResult(CbmMcpJson.FormatSearchGraph(project, search, searchMode));
    }

    [McpServerTool(Name = "get_code_snippet")]
    [Description("Return source lines for a symbol by exact or suffix qualified_name.")]
    public Task<string> GetCodeSnippetAsync(
        [Description("Indexed project name.")] string project,
        [Description("Exact or suffix qualified_name to resolve.")] string qualified_name,
        [Description("Include one-hop caller/callee names when available.")] bool include_neighbors = false)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (string.IsNullOrWhiteSpace(qualified_name))
        {
            throw new McpException("qualified_name is required");
        }

        EnsureProjectIndexed(project);

        var snippet = codeSnippetService.GetSnippet(project, qualified_name);
        if (!snippet.Found)
        {
            if (string.Equals(snippet.MatchType, "ambiguous", StringComparison.Ordinal))
            {
                return Task.FromResult(CbmMcpJson.FormatCodeSnippetNotFound(snippet));
            }

            throw new McpException(CbmMcpJson.FormatCodeSnippetNotFound(snippet));
        }

        using var store = OpenProjectStore(project);
        var node = store.FindNodeByQualifiedName(project, snippet.QualifiedName!)
            ?? store.FindNodesByQualifiedNameSuffix(project, qualified_name).FirstOrDefault();
        if (node is null)
        {
            throw new McpException("symbol not found after snippet resolution");
        }

        return Task.FromResult(CbmMcpJson.FormatCodeSnippet(snippet, node, include_neighbors, store));
    }

    [McpServerTool(Name = "get_graph_schema")]
    [Description("Return node label and edge type counts for an indexed project.")]
    public Task<string> GetGraphSchemaAsync(
        [Description("Indexed project name.")] string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        EnsureProjectIndexed(project);
        var schema = graphSchemaService.GetSchema(project);
        return Task.FromResult(CbmMcpJson.FormatGraphSchema(schema));
    }

    [McpServerTool(Name = "query_graph")]
    [Description("Run a read-only Cypher-subset query against an indexed project graph.")]
    public Task<string> QueryGraphAsync(
        [Description("Indexed project name.")] string project,
        [Description("Cypher query string.")] string query,
        [Description("Maximum rows to return (0 = 100k ceiling).")] int max_rows = 0)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query is required");
        }

        EnsureProjectIndexed(project);

        try
        {
            var result = queryGraphService.Query(project, query, max_rows);
            return Task.FromResult(CbmMcpJson.FormatQueryGraph(result));
        }
        catch (CypherParseException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (CypherPlanException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (CypherExecuteException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "get_architecture")]
    [Description(
        "Get high-level architecture overview — packages, dependencies, hotspots, and project "
        + "structure. Includes Leiden community detection over the CALLS graph. Optional path "
        + "scopes analysis to nodes under that directory prefix. Use aspect runtime for observed "
        + "trace overlay data.")]
    public Task<string> GetArchitectureAsync(
        [Description("Indexed project name.")] string project,
        [Description("Optional directory prefix to scope architecture (e.g. src/foo).")] string? path = null,
        [Description("Aspects to include (e.g. structure, packages, clusters, runtime, all). Omit for all.")] string[]? aspects = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        EnsureProjectIndexed(project);

        try
        {
            var result = graphArchitectureService.GetArchitecture(project, path, aspects);
            return Task.FromResult(CbmMcpJson.FormatArchitecture(result));
        }
        catch (FileNotFoundException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "trace_path")]
    [Description(
        "Trace paths through the code graph. Modes: calls (callers/callees), data_flow "
        + "(CALLS+USAGE+WRITES), cross_service (no-op in C# port — no Route nodes).")]
    public Task<string> TracePathAsync(
        [Description("Indexed project name.")] string project,
        [Description("Function or method name, or exact qualified_name.")] string function_name,
        [Description("Traversal direction: inbound, outbound, or both.")] string direction = "both",
        [Description("Maximum hop depth.")] int depth = 3,
        [Description("Trace mode: calls, data_flow, or cross_service.")] string mode = "calls",
        [Description("Add risk classification (CRITICAL/HIGH/MEDIUM/LOW) per hop.")] bool risk_labels = false,
        [Description("Include test files in results.")] bool include_tests = false,
        [Description("Explicit edge types to follow (overrides mode).")] string[]? edge_types = null,
        string? parameter_name = null)
    {
        _ = parameter_name;

        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (string.IsNullOrWhiteSpace(function_name))
        {
            throw new McpException("function_name is required");
        }

        EnsureProjectIndexed(project);

        var result = tracePathService.Trace(
            project,
            function_name,
            direction,
            mode,
            depth,
            risk_labels,
            include_tests,
            edge_types);

        if (result.Ambiguous)
        {
            return Task.FromResult(CbmMcpJson.FormatTracePathAmbiguous(result));
        }

        if (!result.Found)
        {
            throw new McpException(CbmMcpJson.FormatTracePathNotFound(result));
        }

        return Task.FromResult(CbmMcpJson.FormatTracePath(result));
    }

    [McpServerTool(Name = "search_code")]
    [Description(
        "Graph-augmented code search. Finds text patterns in indexed source, deduplicates "
        + "matches into containing symbols, and ranks by structural importance. Modes: "
        + "compact (default), full (with source), files (paths only).")]
    public Task<string> SearchCodeAsync(
        [Description("Indexed project name.")] string project,
        [Description("Text or regex pattern to search for.")] string pattern,
        [Description("Glob for file names (e.g. *.cs).")] string? file_pattern = null,
        [Description("Regex filter on result file paths (e.g. ^src/).")] string? path_filter = null,
        [Description("Output mode: compact, full, or files.")] string? mode = null,
        [Description("Context lines around each match (compact mode only).")] int context = 0,
        [Description("When true, treat pattern as extended regex.")] bool regex = false,
        [Description("Maximum enriched results to return.")] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new McpException("pattern is required");
        }

        EnsureProjectIndexed(project);

        try
        {
            var result = searchCodeService.Search(
                project,
                pattern,
                file_pattern,
                path_filter,
                mode,
                context,
                regex,
                limit);
            return Task.FromResult(CbmMcpJson.FormatSearchCode(result));
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "manage_adr")]
    [Description("Create or update Architecture Decision Records.")]
    public Task<string> ManageAdrAsync(
        [Description("Indexed project name.")] string project,
        [Description("Operation mode: get, update, store, or sections.")] string? mode = null,
        [Description("Full ADR markdown content for update/store mode.")] string? content = null,
        [Description("Accepted for CBM parity; ignored by the handler.")] string[]? sections = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        _ = sections;

        try
        {
            EnsureProjectIndexed(project);
            var result = manageAdrService.Manage(project, mode, content, sections);
            if (result.IsWriteError)
            {
                throw new McpException(CbmMcpJson.FormatManageAdr(result));
            }

            return Task.FromResult(CbmMcpJson.FormatManageAdr(result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "project not found")
        {
            throw new McpException("project not found");
        }
    }

    [McpServerTool(Name = "ingest_traces")]
    [Description("Ingest runtime traces to enhance the knowledge graph with observed call/latency data.")]
    public Task<string> IngestTracesAsync(
        [Description("Indexed project name.")] string project,
        [Description("Array of trace entries (direct fields or OTLP-like spans).")] JsonElement[] traces)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        if (traces is null)
        {
            throw new McpException("traces is required");
        }

        try
        {
            EnsureProjectIndexed(project);
            var result = ingestTracesService.Ingest(project, traces);
            return Task.FromResult(CbmMcpJson.FormatIngestTraces(result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "project not found")
        {
            throw new McpException("project not found");
        }
    }

    [McpServerTool(Name = "detect_changes")]
    [Description("Detect code changes and their impact via git diff and CALLS graph propagation.")]
    public Task<string> DetectChangesAsync(
        [Description("Indexed project name.")] string project,
        [Description("Result scope: files, symbols (default), or impact.")] string? scope = null,
        [Description("CALLS BFS depth for impact scope.")] int depth = 2,
        [Description("Base branch or ref for three-dot diff (default main).")] string? base_branch = null,
        [Description("Git ref to compare from; takes precedence over base_branch.")] string? since = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new McpException("project is required");
        }

        EnsureProjectIndexed(project);

        var result = detectChangesService.Detect(project, base_branch, since, scope, depth);
        if (!result.Success)
        {
            throw new McpException(CbmMcpJson.FormatDetectChanges(result));
        }

        return Task.FromResult(CbmMcpJson.FormatDetectChanges(result));
    }

    private static void EnsureProjectDatabaseExists(string project)
    {
        var databasePath = CbmCachePaths.GetProjectDatabasePath(project);
        if (!File.Exists(databasePath))
        {
            throw new McpException(
                "{\"error\":\"project not found or not indexed\",\"hint\":\"Call index_repository first.\"}");
        }
    }

    private static void EnsureProjectIndexed(string project)
    {
        EnsureProjectDatabaseExists(project);
        using var store = OpenProjectStore(project);
        if (store.GetProject(project) is null)
        {
            throw new McpException(
                "{\"error\":\"project not indexed — run index_repository first\"}");
        }
    }

    private static CbmStore OpenProjectStore(string projectName)
    {
        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        return CbmStore.OpenPath(databasePath);
    }
}
