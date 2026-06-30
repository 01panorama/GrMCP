# CBM MCP Tools

CBM MCP exposes a local, stdio-based Model Context Protocol server for indexing C# repositories into a SQLite code knowledge graph and querying that graph from agents.

## Setup

Prerequisites:

- Install the .NET SDK required by this build (`net10.0`).
- Install or build the CBM MCP executable from this repository or the internal tool feed.
- Ensure the target repository contains C# source, a `.csproj`, or a `.sln` file. Loose `.cs` files are supported with reduced semantic resolution.

MCP client registration uses stdio. Point your client at the installed command or the local MCP project binary, and set `CBM_CACHE_DIR` if you want indexes outside the default cache location.

```json
{
  "mcpServers": {
    "cbm": {
      "command": "cbm-mcp",
      "args": [],
      "env": {
        "CBM_CACHE_DIR": "/Users/example/.cache/cbm"
      }
    }
  }
}
```

For local development before packaging, run the server from the MCP project output and use the same stdio registration shape with `command` set to `dotnet` and `args` set to `["exec", "/absolute/path/to/Cbm.Mcp.dll"]`.

After registration, smoke test the server by calling `list_tools`, then index a repository:

```json
{
  "name": "index_repository",
  "arguments": {
    "repo_path": "/Users/example/MyApp"
  }
}
```

Use the returned `project` name with `search_graph`, `get_code_snippet`, `query_graph`, and the other project-scoped tools.

Typical workflow:

1. Call `index_repository` with a repository, solution, or project path.
2. Use `list_projects` or `index_status` to find or verify the indexed project name.
3. Use query tools such as `search_graph`, `get_code_snippet`, `search_code`, `query_graph`, `get_architecture`, and `trace_path`.
4. Use mutation tools such as `manage_adr`, `ingest_traces`, and `detect_changes` when you need persisted ADR context, runtime overlays, or git impact analysis.

The cache directory is `CBM_CACHE_DIR` when set, otherwise `~/.cache/graph-mcp-dotnet`.

## Tool Index

- [`list_tools`](#list_tools) - Return rich documentation for every CBM MCP tool.
- [`list_projects`](#list_projects) - List indexed projects in the local CBM cache.
- [`index_repository`](#index_repository) - Index a C# repository into the knowledge graph.
- [`index_status`](#index_status) - Return node and edge counts plus status for an indexed project.
- [`delete_project`](#delete_project) - Delete a project's cached index database.
- [`search_graph`](#search_graph) - Search the code knowledge graph using BM25 query and/or regex filters.
- [`get_code_snippet`](#get_code_snippet) - Return source lines for a symbol by exact or suffix qualified_name.
- [`search_code`](#search_code) - Graph-augmented code search over indexed source files.
- [`query_graph`](#query_graph) - Run a read-only Cypher-subset query against an indexed project graph.
- [`get_graph_schema`](#get_graph_schema) - Return node label and edge type counts for an indexed project.
- [`get_architecture`](#get_architecture) - Get a high-level architecture overview for an indexed project.
- [`trace_path`](#trace_path) - Trace paths through the code graph for calls, data flow, or cross-service compatibility mode.
- [`detect_changes`](#detect_changes) - Detect code changes and their impact via git diff and CALLS graph propagation.
- [`manage_adr`](#manage_adr) - Create, update, retrieve, or inspect Architecture Decision Records.
- [`ingest_traces`](#ingest_traces) - Ingest runtime traces to enhance the knowledge graph with observed call and latency data.

## list_tools

Return rich documentation for every CBM MCP tool.

Category: `meta`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `format` | `string` | no | Output format: json (default) or markdown. |
| `name` | `string` | no | Optional exact tool name filter. |
| `tools` | `string[]` | no | Optional exact tool names filter for multiple relevant tools. |
| `category` | `string` | no | Optional category filter: lifecycle, query, mutation, or meta. |

### Usage

Use this first when you need examples, caveats, or parameter details beyond the MCP tools/list schema. Omit filters for the full catalog, pass `name` for one tool, `tools` for several exact tools, or `category` for a broad slice.

### Example Input

```json
{
  "format": "json",
  "tools": ["search_graph", "get_code_snippet"]
}
```

### Example Output

```json
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
```

### MCP Invocation

```json
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
```

## list_projects

List indexed projects in the local CBM cache.

Category: `lifecycle`

### Parameters

This tool has no parameters.

### Usage

Use this to discover available project names before calling tools that require project.

### Example Input

```json
{}
```

### Example Output

```json
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
```

### MCP Invocation

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "list_projects",
    "arguments": {}
  }
}
```

### Caveats

- Returns a hint when no projects are indexed.

## index_repository

Index a C# repository into the knowledge graph.

Category: `lifecycle`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `repo_path` | `string` | yes | Path to the repository root, solution, or project file. |
| `mode` | `string` | no | Indexing mode. cross-repo-intelligence is rejected in this C# port. |

### Usage

Run this before query tools. The index is stored in `CBM_CACHE_DIR` when set, otherwise in the local CBM cache.

### Example Input

```json
{
  "repo_path": "/Users/example/MyApp",
  "mode": "full"
}
```

### Example Output

```json
{
  "project": "Users-example-MyApp",
  "status": "indexed",
  "nodes": 128,
  "edges": 256,
  "root_path": "/Users/example/MyApp",
  "index_mode": "full"
}
```

### MCP Invocation

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "index_repository",
    "arguments": { "repo_path": "/Users/example/MyApp" }
  }
}
```

### Caveats

- Requires a C# project, solution, or loose C# files.

## index_status

Return node and edge counts plus status for an indexed project.

Category: `lifecycle`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |

### Usage

Use this after `index_repository` to verify that a project is populated.

### Example Input

```json
{
  "project": "Users-example-MyApp"
}
```

### Example Output

```json
{
  "project": "Users-example-MyApp",
  "nodes": 128,
  "edges": 256,
  "status": "indexed",
  "root_path": "/Users/example/MyApp"
}
```

### MCP Invocation

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "index_status",
    "arguments": { "project": "Users-example-MyApp" }
  }
}
```

### Caveats

- Throws project-not-found guidance when the cache database is missing.

## delete_project

Delete a project's cached index database.

Category: `lifecycle`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |

### Usage

Use this to remove a stale or corrupt local index before rebuilding.

### Example Input

```json
{
  "project": "Users-example-MyApp"
}
```

### Example Output

```json
{
  "project": "Users-example-MyApp",
  "status": "deleted"
}
```

### MCP Invocation

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "delete_project",
    "arguments": { "project": "Users-example-MyApp" }
  }
}
```

### Caveats

- This deletes only the local CBM cache database, not source files.

## search_graph

Search the code knowledge graph using BM25 query and/or regex filters.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `query` | `string` | no | BM25 full-text query. |
| `label` | `string` | no | Node label filter, such as Method or Class. |
| `name_pattern` | `string` | no | Regex matched against node name. |
| `qn_pattern` | `string` | no | Regex matched against qualified_name. |
| `file_pattern` | `string` | no | Regex matched against file_path. |
| `case_sensitive` | `bool` | no | When true, regex matching is case-sensitive. |
| `limit` | `int` | no | Maximum results to return. |
| `offset` | `int` | no | Number of matching nodes to skip before returning results. |
| `semantic_query` | `string[]` | no | Unsupported compatibility parameter. |

### Usage

Use `query` for broad discovery, then narrow with `label`, `name_pattern`, `qn_pattern`, or `file_pattern`.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "name_pattern": "Execute",
  "label": "Method",
  "limit": 5
}
```

### Example Output

```json
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
```

### MCP Invocation

```json
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
```

### Caveats

- `semantic_query` is not supported in the C# port because embeddings are out of scope.

## get_code_snippet

Return source lines for a symbol by exact or suffix qualified_name.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `qualified_name` | `string` | yes | Exact or suffix qualified_name to resolve. |
| `include_neighbors` | `bool` | no | Include one-hop caller/callee names when available. |

### Usage

Use `search_graph` first to find a `qualified_name`, then call this tool for source context.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "qualified_name": "Sample.Worker.Execute",
  "include_neighbors": true
}
```

### Example Output

```json
{
  "qualified_name": "Sample.Worker.Execute",
  "source": "public string Execute()\\n{\\n    return \"ok\";\\n}",
  "start_line": 5,
  "end_line": 8
}
```

### MCP Invocation

```json
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
```

### Caveats

- Ambiguous suffix matches return suggestions instead of source.

## search_code

Graph-augmented code search over indexed source files.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `pattern` | `string` | yes | Text or regex pattern to search for. |
| `file_pattern` | `string` | no | Glob for file names, such as *.cs. |
| `path_filter` | `string` | no | Regex filter on result file paths. |
| `mode` | `string` | no | Output mode: compact, full, or files. |
| `context` | `int` | no | Context lines around each match in compact mode. |
| `regex` | `bool` | no | When true, treat pattern as extended regex. |
| `limit` | `int` | no | Maximum enriched results to return. |

### Usage

Use this for text search when you want matches grouped by containing symbol and ranked by graph context.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "pattern": "Target",
  "mode": "compact"
}
```

### Example Output

```json
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
```

### MCP Invocation

```json
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
```

### Caveats

- Implemented as pure .NET scanning, not shell grep.

## query_graph

Run a read-only Cypher-subset query against an indexed project graph.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `query` | `string` | yes | Cypher query string. |
| `max_rows` | `int` | no | Maximum rows to return. Zero uses the 100k ceiling. |

### Usage

Use this for precise graph questions over labels, edge types, and node properties.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "query": "MATCH (n:Method) RETURN n.name LIMIT 10"
}
```

### Example Output

```json
{
  "columns": ["n.name"],
  "rows": [["Execute"], ["Target"]],
  "total": 2
}
```

### MCP Invocation

```json
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
```

### Caveats

- Only the supported read-only Cypher subset is accepted.

## get_graph_schema

Return node label and edge type counts for an indexed project.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |

### Usage

Use this before writing `query_graph` queries to discover labels and edge types present in the project.

### Example Input

```json
{
  "project": "Users-example-MyApp"
}
```

### Example Output

```json
{
  "node_labels": [
    { "label": "Method", "count": 42, "properties": [] }
  ],
  "edge_types": [
    { "type": "CALLS", "count": 64, "properties": [] }
  ]
}
```

### MCP Invocation

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "get_graph_schema",
    "arguments": { "project": "Users-example-MyApp" }
  }
}
```

### Caveats

- Property lists are currently empty placeholders in this C# port.

## get_architecture

Get a high-level architecture overview for an indexed project.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `path` | `string` | no | Optional directory prefix to scope architecture. |
| `aspects` | `string[]` | no | Aspects to include, such as structure, packages, clusters, runtime, or all. |

### Usage

Use this for package counts, dependency shape, hotspots, clusters, file tree, and optional runtime trace overlay.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "aspects": ["structure", "clusters"]
}
```

### Example Output

```json
{
  "project": "Users-example-MyApp",
  "total_nodes": 128,
  "total_edges": 256,
  "languages": [
    { "language": "C#", "file_count": 12 }
  ]
}
```

### MCP Invocation

```json
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
```

### Caveats

- Clustering runs on the CALLS graph only.
- Runtime data appears only after `ingest_traces`.

## trace_path

Trace paths through the code graph for calls, data flow, or cross-service compatibility mode.

Category: `query`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `function_name` | `string` | yes | Function or method name, or exact qualified_name. |
| `direction` | `string` | no | Traversal direction: inbound, outbound, or both. |
| `depth` | `int` | no | Maximum hop depth. |
| `mode` | `string` | no | Trace mode: calls, data_flow, or cross_service. |
| `risk_labels` | `bool` | no | Add risk classification per hop. |
| `include_tests` | `bool` | no | Include test files in results. |
| `edge_types` | `string[]` | no | Explicit edge types to follow, overriding mode. |
| `parameter_name` | `string` | no | Accepted for CBM parity; not used by the handler. |

### Usage

Use this after finding a method to inspect callers, callees, or nearby data-flow edges.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "function_name": "Target",
  "direction": "inbound",
  "depth": 3
}
```

### Example Output

```json
{
  "function": "Target",
  "direction": "inbound",
  "mode": "calls",
  "callers": [
    { "name": "Run", "qualified_name": "Sample.Caller.Run", "hop": 1 }
  ]
}
```

### MCP Invocation

```json
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
```

### Caveats

- `cross_service` is a no-op in the C# port because Route nodes are out of scope.

## detect_changes

Detect code changes and their impact via git diff and CALLS graph propagation.

Category: `mutation`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `scope` | `string` | no | Result scope: files, symbols, or impact. |
| `depth` | `int` | no | CALLS BFS depth for impact scope. |
| `base_branch` | `string` | no | Base branch or ref for three-dot diff. |
| `since` | `string` | no | Git ref to compare from; takes precedence over base_branch. |

### Usage

Use this in git repositories to find changed files, changed symbols, or impacted callers.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "scope": "impact",
  "depth": 2,
  "base_branch": "main"
}
```

### Example Output

```json
{
  "changed_files": ["Worker.cs"],
  "changed_count": 1,
  "depth": 2,
  "base": "main",
  "scope": "impact",
  "impacted_symbol_count": 3
}
```

### MCP Invocation

```json
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
```

### Caveats

- Non-git repositories return a `not_a_git_repo` error.
- `symbols` scope reports changed-file symbols only; use `impact` for propagation.

## manage_adr

Create, update, retrieve, or inspect Architecture Decision Records.

Category: `mutation`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `mode` | `string` | no | Operation mode: get, update, store, or sections. |
| `content` | `string` | no | Full ADR markdown content for update or store mode. |
| `sections` | `string[]` | no | Accepted for CBM parity; ignored by the handler. |

### Usage

Use this to keep architecture context near the project graph for future agent sessions.

### Example Input

```json
{
  "project": "Users-example-MyApp",
  "mode": "update",
  "content": "## PURPOSE\\nDocument key architecture decisions.\\n"
}
```

### Example Output

```json
{
  "status": "updated"
}
```

### MCP Invocation

```json
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
```

### Caveats

- There is no delete mode.
- The `sections` argument is currently ignored by the handler.

## ingest_traces

Ingest runtime traces to enhance the knowledge graph with observed call and latency data.

Category: `mutation`

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | `string` | yes | Indexed project name. |
| `traces` | `JsonElement[]` | yes | Array of trace entries with direct fields or OTLP-like spans. |

### Usage

Use this after indexing to overlay observed runtime calls, counts, and latency data onto static CALLS edges.

### Example Input

```json
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
```

### Example Output

```json
{
  "status": "accepted",
  "traces_received": 1,
  "traces_ingested": 1,
  "edges_matched": 1,
  "unresolved": 0
}
```

### MCP Invocation

```json
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
```

### Caveats

- Route nodes are out of scope, so route observations do not create cross-service graph completeness.
