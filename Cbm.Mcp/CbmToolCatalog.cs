using System.Text;

namespace Cbm.Mcp;

public sealed record CbmToolParameter(
    string Name,
    string Type,
    bool Required,
    string Description);

public sealed record CbmToolDefinition(
    string Name,
    string Category,
    string Description,
    IReadOnlyList<CbmToolParameter> Parameters,
    string Usage,
    string ExampleInput,
    string ExampleOutput,
    string CodeSnippet,
    IReadOnlyList<string> Caveats);

public static class CbmToolCatalog
{
    public static IReadOnlyList<string> Categories { get; } = ["lifecycle", "query", "mutation", "meta"];

    public static IReadOnlyList<CbmToolDefinition> Tools { get; } =
    [
        new(
            "list_tools",
            "meta",
            "Return rich documentation for every CBM MCP tool.",
            [
                new("format", "string", false, "Output format: json (default) or markdown."),
                new("name", "string", false, "Optional exact tool name filter."),
                new("tools", "string[]", false, "Optional exact tool names filter for multiple relevant tools."),
                new("category", "string", false, "Optional category filter: lifecycle, query, mutation, or meta."),
            ],
            "Use this first when you need examples, caveats, or parameter details beyond the MCP tools/list schema. Omit filters for the full catalog, pass name for one tool, tools for several exact tools, or category for a broad slice.",
            """
            {
              "format": "json",
              "tools": ["search_graph", "get_code_snippet"]
            }
            """,
            """
            {
              "total": 2,
              "tools": [
                {
                  "name": "search_graph",
                  "category": "query",
                  "description": "Search the code knowledge graph using BM25 query and/or regex filters."
                },
                {
                  "name": "get_code_snippet",
                  "category": "query",
                  "description": "Return source lines for a symbol by exact or suffix qualified_name."
                }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "tools/call",
              "params": {
                "name": "list_tools",
                "arguments": {
                  "format": "json",
                  "tools": ["search_graph", "get_code_snippet"]
                }
              }
            }
            """,
            []),
        new(
            "list_projects",
            "lifecycle",
            "List indexed projects in the local CBM cache.",
            [],
            "Use this to discover available project names before calling tools that require project.",
            """
            {}
            """,
            """
            {
              "projects": [
                {
                  "name": "Users-example-MyApp",
                  "root_path": "/Users/example/MyApp",
                  "nodes": 128,
                  "edges": 256,
                  "size_bytes": 1048576
                }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 2,
              "method": "tools/call",
              "params": {
                "name": "list_projects",
                "arguments": {}
              }
            }
            """,
            ["Returns a hint when no projects are indexed."]),
        new(
            "index_repository",
            "lifecycle",
            "Index a C# repository into the knowledge graph.",
            [
                new("repo_path", "string", true, "Path to the repository root, solution, or project file."),
                new("mode", "string", false, "Indexing mode. cross-repo-intelligence is rejected in this C# port."),
            ],
            "Run this before query tools. The index is stored in CBM_CACHE_DIR when set, otherwise in the local CBM cache.",
            """
            {
              "repo_path": "/Users/example/MyApp",
              "mode": "full"
            }
            """,
            """
            {
              "project": "Users-example-MyApp",
              "status": "indexed",
              "nodes": 128,
              "edges": 256,
              "root_path": "/Users/example/MyApp",
              "index_mode": "full"
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 3,
              "method": "tools/call",
              "params": {
                "name": "index_repository",
                "arguments": { "repo_path": "/Users/example/MyApp" }
              }
            }
            """,
            ["Requires a C# project, solution, or loose C# files."]),
        new(
            "index_status",
            "lifecycle",
            "Return node and edge counts plus status for an indexed project.",
            [
                new("project", "string", true, "Indexed project name."),
            ],
            "Use this after index_repository to verify that a project is populated.",
            """
            {
              "project": "Users-example-MyApp"
            }
            """,
            """
            {
              "project": "Users-example-MyApp",
              "nodes": 128,
              "edges": 256,
              "status": "indexed",
              "root_path": "/Users/example/MyApp"
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 4,
              "method": "tools/call",
              "params": {
                "name": "index_status",
                "arguments": { "project": "Users-example-MyApp" }
              }
            }
            """,
            ["Throws project-not-found guidance when the cache database is missing."]),
        new(
            "delete_project",
            "lifecycle",
            "Delete a project's cached index database.",
            [
                new("project", "string", true, "Indexed project name."),
            ],
            "Use this to remove a stale or corrupt local index before rebuilding.",
            """
            {
              "project": "Users-example-MyApp"
            }
            """,
            """
            {
              "project": "Users-example-MyApp",
              "status": "deleted"
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 5,
              "method": "tools/call",
              "params": {
                "name": "delete_project",
                "arguments": { "project": "Users-example-MyApp" }
              }
            }
            """,
            ["This deletes only the local CBM cache database, not source files."]),
        new(
            "search_graph",
            "query",
            "Search the code knowledge graph using BM25 query and/or regex filters.",
            [
                new("project", "string", true, "Indexed project name."),
                new("query", "string", false, "BM25 full-text query."),
                new("label", "string", false, "Node label filter, such as Method or Class."),
                new("name_pattern", "string", false, "Regex matched against node name."),
                new("qn_pattern", "string", false, "Regex matched against qualified_name."),
                new("file_pattern", "string", false, "Regex matched against file_path."),
                new("case_sensitive", "bool", false, "When true, regex matching is case-sensitive."),
                new("limit", "int", false, "Maximum results to return."),
                new("offset", "int", false, "Number of matching nodes to skip before returning results."),
                new("semantic_query", "string[]", false, "Unsupported compatibility parameter."),
            ],
            "Use query for broad discovery, then narrow with label, name_pattern, qn_pattern, or file_pattern.",
            """
            {
              "project": "Users-example-MyApp",
              "name_pattern": "Execute",
              "label": "Method",
              "limit": 5
            }
            """,
            """
            {
              "total": 1,
              "has_more": false,
              "results": [
                {
                  "name": "Execute",
                  "qualified_name": "Sample.Worker.Execute",
                  "label": "Method"
                }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 6,
              "method": "tools/call",
              "params": {
                "name": "search_graph",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "name_pattern": "Execute",
                  "limit": 5
                }
              }
            }
            """,
            ["semantic_query is not supported in the C# port because embeddings are out of scope."]),
        new(
            "get_code_snippet",
            "query",
            "Return source lines for a symbol by exact or suffix qualified_name.",
            [
                new("project", "string", true, "Indexed project name."),
                new("qualified_name", "string", true, "Exact or suffix qualified_name to resolve."),
                new("include_neighbors", "bool", false, "Include one-hop caller/callee names when available."),
            ],
            "Use search_graph first to find a qualified_name, then call this tool for source context.",
            """
            {
              "project": "Users-example-MyApp",
              "qualified_name": "Sample.Worker.Execute",
              "include_neighbors": true
            }
            """,
            """
            {
              "qualified_name": "Sample.Worker.Execute",
              "source": "public string Execute()\\n{\\n    return \"ok\";\\n}",
              "start_line": 5,
              "end_line": 8
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 7,
              "method": "tools/call",
              "params": {
                "name": "get_code_snippet",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "qualified_name": "Sample.Worker.Execute"
                }
              }
            }
            """,
            ["Ambiguous suffix matches return suggestions instead of source."]),
        new(
            "search_code",
            "query",
            "Graph-augmented code search over indexed source files.",
            [
                new("project", "string", true, "Indexed project name."),
                new("pattern", "string", true, "Text or regex pattern to search for."),
                new("file_pattern", "string", false, "Glob for file names, such as *.cs."),
                new("path_filter", "string", false, "Regex filter on result file paths."),
                new("mode", "string", false, "Output mode: compact, full, or files."),
                new("context", "int", false, "Context lines around each match in compact mode."),
                new("regex", "bool", false, "When true, treat pattern as extended regex."),
                new("limit", "int", false, "Maximum enriched results to return."),
            ],
            "Use this for text search when you want matches grouped by containing symbol and ranked by graph context.",
            """
            {
              "project": "Users-example-MyApp",
              "pattern": "Target",
              "mode": "compact"
            }
            """,
            """
            {
              "total_grep_matches": 1,
              "total_results": 1,
              "results": [
                {
                  "node": "Target",
                  "qualified_name": "Sample.Callee.Target"
                }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 8,
              "method": "tools/call",
              "params": {
                "name": "search_code",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "pattern": "Target"
                }
              }
            }
            """,
            ["Implemented as pure .NET scanning, not shell grep."]),
        new(
            "query_graph",
            "query",
            "Run a read-only Cypher-subset query against an indexed project graph.",
            [
                new("project", "string", true, "Indexed project name."),
                new("query", "string", true, "Cypher query string."),
                new("max_rows", "int", false, "Maximum rows to return. Zero uses the 100k ceiling."),
            ],
            "Use this for precise graph questions over labels, edge types, and node properties.",
            """
            {
              "project": "Users-example-MyApp",
              "query": "MATCH (n:Method) RETURN n.name LIMIT 10"
            }
            """,
            """
            {
              "columns": ["n.name"],
              "rows": [["Execute"], ["Target"]],
              "total": 2
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 9,
              "method": "tools/call",
              "params": {
                "name": "query_graph",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "query": "MATCH (n:Method) RETURN n.name LIMIT 10"
                }
              }
            }
            """,
            ["Only the supported read-only Cypher subset is accepted."]),
        new(
            "get_graph_schema",
            "query",
            "Return node label and edge type counts for an indexed project.",
            [
                new("project", "string", true, "Indexed project name."),
            ],
            "Use this before writing query_graph queries to discover labels and edge types present in the project.",
            """
            {
              "project": "Users-example-MyApp"
            }
            """,
            """
            {
              "node_labels": [
                { "label": "Method", "count": 42, "properties": [] }
              ],
              "edge_types": [
                { "type": "CALLS", "count": 64, "properties": [] }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 10,
              "method": "tools/call",
              "params": {
                "name": "get_graph_schema",
                "arguments": { "project": "Users-example-MyApp" }
              }
            }
            """,
            ["Property lists are currently empty placeholders in this C# port."]),
        new(
            "get_architecture",
            "query",
            "Get a high-level architecture overview for an indexed project.",
            [
                new("project", "string", true, "Indexed project name."),
                new("path", "string", false, "Optional directory prefix to scope architecture."),
                new("aspects", "string[]", false, "Aspects to include, such as structure, packages, clusters, runtime, or all."),
            ],
            "Use this for package counts, dependency shape, hotspots, clusters, file tree, and optional runtime trace overlay.",
            """
            {
              "project": "Users-example-MyApp",
              "aspects": ["structure", "clusters"]
            }
            """,
            """
            {
              "project": "Users-example-MyApp",
              "total_nodes": 128,
              "total_edges": 256,
              "languages": [
                { "language": "C#", "file_count": 12 }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 11,
              "method": "tools/call",
              "params": {
                "name": "get_architecture",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "aspects": ["all"]
                }
              }
            }
            """,
            ["Clustering runs on the CALLS graph only.", "Runtime data appears only after ingest_traces."]),
        new(
            "trace_path",
            "query",
            "Trace paths through the code graph for calls, data flow, or cross-service compatibility mode.",
            [
                new("project", "string", true, "Indexed project name."),
                new("function_name", "string", true, "Function or method name, or exact qualified_name."),
                new("direction", "string", false, "Traversal direction: inbound, outbound, or both."),
                new("depth", "int", false, "Maximum hop depth."),
                new("mode", "string", false, "Trace mode: calls, data_flow, or cross_service."),
                new("risk_labels", "bool", false, "Add risk classification per hop."),
                new("include_tests", "bool", false, "Include test files in results."),
                new("edge_types", "string[]", false, "Explicit edge types to follow, overriding mode."),
                new("parameter_name", "string", false, "Accepted for CBM parity; not used by the handler."),
            ],
            "Use this after finding a method to inspect callers, callees, or nearby data-flow edges.",
            """
            {
              "project": "Users-example-MyApp",
              "function_name": "Target",
              "direction": "inbound",
              "depth": 3
            }
            """,
            """
            {
              "function": "Target",
              "direction": "inbound",
              "mode": "calls",
              "callers": [
                { "name": "Run", "qualified_name": "Sample.Caller.Run", "hop": 1 }
              ]
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 12,
              "method": "tools/call",
              "params": {
                "name": "trace_path",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "function_name": "Target",
                  "direction": "inbound"
                }
              }
            }
            """,
            ["cross_service is a no-op in the C# port because Route nodes are out of scope."]),
        new(
            "detect_changes",
            "mutation",
            "Detect code changes and their impact via git diff and CALLS graph propagation.",
            [
                new("project", "string", true, "Indexed project name."),
                new("scope", "string", false, "Result scope: files, symbols, or impact."),
                new("depth", "int", false, "CALLS BFS depth for impact scope."),
                new("base_branch", "string", false, "Base branch or ref for three-dot diff."),
                new("since", "string", false, "Git ref to compare from; takes precedence over base_branch."),
            ],
            "Use this in git repositories to find changed files, changed symbols, or impacted callers.",
            """
            {
              "project": "Users-example-MyApp",
              "scope": "impact",
              "depth": 2,
              "base_branch": "main"
            }
            """,
            """
            {
              "changed_files": ["Worker.cs"],
              "changed_count": 1,
              "depth": 2,
              "base": "main",
              "scope": "impact",
              "impacted_symbol_count": 3
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 13,
              "method": "tools/call",
              "params": {
                "name": "detect_changes",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "scope": "impact",
                  "depth": 2
                }
              }
            }
            """,
            ["Non-git repositories return a not_a_git_repo error.", "symbols scope reports changed-file symbols only; use impact for propagation."]),
        new(
            "manage_adr",
            "mutation",
            "Create, update, retrieve, or inspect Architecture Decision Records.",
            [
                new("project", "string", true, "Indexed project name."),
                new("mode", "string", false, "Operation mode: get, update, store, or sections."),
                new("content", "string", false, "Full ADR markdown content for update or store mode."),
                new("sections", "string[]", false, "Accepted for CBM parity; ignored by the handler."),
            ],
            "Use this to keep architecture context near the project graph for future agent sessions.",
            """
            {
              "project": "Users-example-MyApp",
              "mode": "update",
              "content": "## PURPOSE\\nDocument key architecture decisions.\\n"
            }
            """,
            """
            {
              "status": "updated"
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 14,
              "method": "tools/call",
              "params": {
                "name": "manage_adr",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "mode": "sections"
                }
              }
            }
            """,
            ["There is no delete mode.", "The sections argument is currently ignored by the handler."]),
        new(
            "ingest_traces",
            "mutation",
            "Ingest runtime traces to enhance the knowledge graph with observed call and latency data.",
            [
                new("project", "string", true, "Indexed project name."),
                new("traces", "JsonElement[]", true, "Array of trace entries with direct fields or OTLP-like spans."),
            ],
            "Use this after indexing to overlay observed runtime calls, counts, and latency data onto static CALLS edges.",
            """
            {
              "project": "Users-example-MyApp",
              "traces": [
                {
                  "caller": "Run",
                  "callee": "Target",
                  "duration_ms": 8.0,
                  "count": 1
                }
              ]
            }
            """,
            """
            {
              "status": "accepted",
              "traces_received": 1,
              "traces_ingested": 1,
              "edges_matched": 1,
              "unresolved": 0
            }
            """,
            """
            {
              "jsonrpc": "2.0",
              "id": 15,
              "method": "tools/call",
              "params": {
                "name": "ingest_traces",
                "arguments": {
                  "project": "Users-example-MyApp",
                  "traces": [
                    {
                      "caller": "Run",
                      "callee": "Target",
                      "duration_ms": 8.0,
                      "count": 1
                    }
                  ]
                }
              }
            }
            """,
            ["Route nodes are out of scope, so route observations do not create cross-service graph completeness."]),
    ];

    public static CbmToolDefinition? FindByName(string name)
    {
        return Tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
    }

    public static string RenderMarkdown(IEnumerable<CbmToolDefinition>? tools = null)
    {
        var selectedTools = (tools ?? Tools).ToArray();
        var includeFullOverview = selectedTools.Length == Tools.Count
            && selectedTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(Tools.Select(tool => tool.Name));
        var builder = new StringBuilder();

        builder.AppendLine("# CBM MCP Tools");
        builder.AppendLine();
        if (includeFullOverview)
        {
            AppendFullOverview(builder);
        }
        else
        {
            builder.AppendLine("Filtered tool documentation for the requested CBM MCP tools.");
            builder.AppendLine();
        }

        builder.AppendLine("## Tool Index");
        builder.AppendLine();

        foreach (var tool in selectedTools)
        {
            builder.AppendLine($"- [`{tool.Name}`](#{ToAnchor(tool.Name)}) - {tool.Description}");
        }

        foreach (var tool in selectedTools)
        {
            AppendTool(builder, tool);
        }

        return builder.ToString();
    }

    private static void AppendTool(StringBuilder builder, CbmToolDefinition tool)
    {
        builder.AppendLine();
        builder.AppendLine($"## {tool.Name}");
        builder.AppendLine();
        builder.AppendLine(tool.Description);
        builder.AppendLine();
        builder.AppendLine($"Category: `{tool.Category}`");
        builder.AppendLine();
        builder.AppendLine("### Parameters");
        builder.AppendLine();

        if (tool.Parameters.Count == 0)
        {
            builder.AppendLine("This tool has no parameters.");
        }
        else
        {
            builder.AppendLine("| Name | Type | Required | Description |");
            builder.AppendLine("|------|------|----------|-------------|");
            foreach (var parameter in tool.Parameters)
            {
                builder.AppendLine(
                    $"| `{parameter.Name}` | `{parameter.Type}` | {(parameter.Required ? "yes" : "no")} | {EscapeTableCell(parameter.Description)} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("### Usage");
        builder.AppendLine();
        builder.AppendLine(tool.Usage);
        builder.AppendLine();
        builder.AppendLine("### Example Input");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(tool.ExampleInput.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("### Example Output");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(tool.ExampleOutput.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("### MCP Invocation");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(tool.CodeSnippet.Trim());
        builder.AppendLine("```");

        if (tool.Caveats.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("### Caveats");
        builder.AppendLine();
        foreach (var caveat in tool.Caveats)
        {
            builder.AppendLine($"- {caveat}");
        }
    }

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static void AppendFullOverview(StringBuilder builder)
    {
        builder.AppendLine("CBM MCP exposes a local, stdio-based Model Context Protocol server for indexing C# repositories into a SQLite code knowledge graph and querying that graph from agents.");
        builder.AppendLine();
        builder.AppendLine("## Setup");
        builder.AppendLine();
        builder.AppendLine("Prerequisites:");
        builder.AppendLine();
        builder.AppendLine("- Install the .NET SDK required by this build (`net10.0`).");
        builder.AppendLine("- Install or build the CBM MCP executable from this repository or the internal tool feed.");
        builder.AppendLine("- Ensure the target repository contains C# source, a `.csproj`, or a `.sln` file. Loose `.cs` files are supported with reduced semantic resolution.");
        builder.AppendLine();
        builder.AppendLine("MCP client registration uses stdio. Point your client at the installed command or the local MCP project binary, and set `CBM_CACHE_DIR` if you want indexes outside the default cache location.");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"mcpServers\": {");
        builder.AppendLine("    \"cbm\": {");
        builder.AppendLine("      \"command\": \"cbm-mcp\",");
        builder.AppendLine("      \"args\": [],");
        builder.AppendLine("      \"env\": {");
        builder.AppendLine("        \"CBM_CACHE_DIR\": \"/Users/example/.cache/cbm\"");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("For local development before packaging, run the server from the MCP project output and use the same stdio registration shape with `command` set to `dotnet` and `args` set to `[\"exec\", \"/absolute/path/to/Cbm.Mcp.dll\"]`.");
        builder.AppendLine();
        builder.AppendLine("After registration, smoke test the server by calling `list_tools`, then index a repository:");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"name\": \"index_repository\",");
        builder.AppendLine("  \"arguments\": {");
        builder.AppendLine("    \"repo_path\": \"/Users/example/MyApp\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Use the returned `project` name with `search_graph`, `get_code_snippet`, `query_graph`, and the other project-scoped tools.");
        builder.AppendLine();
        builder.AppendLine("Typical workflow:");
        builder.AppendLine();
        builder.AppendLine("1. Call `index_repository` with a repository, solution, or project path.");
        builder.AppendLine("2. Use `list_projects` or `index_status` to find or verify the indexed project name.");
        builder.AppendLine("3. Use query tools such as `search_graph`, `get_code_snippet`, `search_code`, `query_graph`, `get_architecture`, and `trace_path`.");
        builder.AppendLine("4. Use mutation tools such as `manage_adr`, `ingest_traces`, and `detect_changes` when you need persisted ADR context, runtime overlays, or git impact analysis.");
        builder.AppendLine();
        builder.AppendLine("The cache directory is `CBM_CACHE_DIR` when set, otherwise `~/.cache/codebase-memory-mcp-dotnet`.");
        builder.AppendLine();
    }

    private static string ToAnchor(string toolName)
    {
        return toolName.ToLowerInvariant();
    }
}
