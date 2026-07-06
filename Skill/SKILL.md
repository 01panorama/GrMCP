---
name: cbm-mcp
description: Teaches agents how to use the CBM / cbm-mcp MCP server to index_repository C# repositories into a code knowledge graph and query it with search_graph, trace_path, query_graph, and detect_changes. Applies when investigating C# architecture, symbol lookup, call graph traversal, graph queries, and git impact analysis through user-cbm-mcp.
---

# CBM MCP

Use this skill when exploring C# repositories through the `cbm` or `user-cbm-mcp` MCP server. 

## Principles

- Treat CBM as a graph DB over the codebase, not a replacement for reading arbitrary files.
- Prefer graph answers for relationships and architecture, then read live files when exact source text matters.
- Keep `project`, `qualified_name`, and `index_mode` terminology consistent.

## When To Use CBM Vs Native Tools

Prefer CBM for:

- Symbol lookup across indexed C# code.
- Call chains, callers, callees, impact paths, and call graph questions.
- Architecture orientation, package/namespace shape, clusters, hotspots, and graph schema.
- `query_graph` questions that need node labels, edge types, or Cypher-subset filtering.
- Git impact on the indexed graph through `detect_changes`.

Prefer native `Read`, `rg`, or IDE tools for:

- Unindexed repositories or unsupported languages.
- Non-C# files, config, docs, build scripts, and repo metadata.
- Verifying live file content after edits.
- Exact text that may have changed since the last index.

## Prerequisites Checklist

- [ ] MCP server is registered over stdio as `cbm` or `user-cbm-mcp`.
- [ ] .NET SDK required by the server is installed.
- [ ] Target repo has a `.sln` or `.csproj` for Roslyn resolution.
- [ ] Loose `.cs` files are acceptable only with reduced cross-file semantics.
- [ ] Optional: `CBM_CACHE_DIR` is set if indexes should live outside the default cache path.

## Session Bootstrap Workflow

1. Discover existing indexes:

```json
{
  "name": "list_projects",
  "arguments": {}
}
```

2. If the repo is missing, stale, or unknown, index it and save the returned `project`:

```json
{
  "name": "index_repository",
  "arguments": {
    "repo_path": "/Users/example/MyApp"
  }
}
```

3. If a `project` is known, verify it with `index_status`.
4. For first-time orientation, call `get_graph_schema`, then `get_architecture`.
5. For unfamiliar tools or parameter details, call `list_tools`:

```json
{
  "name": "list_tools",
  "arguments": {
    "format": "markdown",
    "tools": ["search_graph", "get_code_snippet", "trace_path"]
  }
}
```

## Investigation Routing Table

| Question type | Start with | Next step |
| --- | --- | --- |
| Find a symbol | `search_graph` with `label`, `name_pattern`, `qn_pattern`, `file_pattern`, or `query` | Use the returned `qualified_name` with `get_code_snippet` |
| Text in source | `search_code` | Narrow with `path_filter` or verify live text with native read tools |
| Show code | `get_code_snippet` with `qualified_name` | Use suffix match only when unique; follow suggestions on ambiguity |
| Call chain | `trace_path` | Disambiguate the target like `get_code_snippet`; tune `direction` and `depth` |
| Structural, dead code, hotspots | `query_graph` | Call `get_graph_schema` first if labels or edges are unknown |
| Git delta or impact | `detect_changes` with scope `files`, `symbols`, or `impact` | Re-index first if changed-file symbols need current source |
| Persist architecture notes | `manage_adr` | Store only durable decisions, not transient findings |
| Runtime overlay | `ingest_traces` | Re-run `get_architecture` with runtime aspects after ingest |

## Standard Investigation Chain

Default pipeline:

1. `search_graph` to find candidate symbols.
2. `get_code_snippet` with `include_neighbors=true` to inspect source and one-hop callers/callees.
3. `trace_path` for caller/callee traversal, or `query_graph` for precise structural questions.
4. Read live files only when you need exact current text, non-C# context, or post-edit verification.

## Qualified Names

- Format: `{project}.{Namespace.Type.Member(params)}`.
- Always pass the `project` returned by `index_repository`, `list_projects`, or `index_status`.
- Use `search_graph` before `get_code_snippet` or `trace_path` when the target is not exact.
- Never guess when a tool returns suggestions. Tighten filters or ask the user which symbol they mean.

## Indexing & Freshness

`index_repository` is the only indexing workflow. Do not expose incremental indexing as a separate MCP tool, separate skill, or manual flow.

### Incremental Indexing

- The C# port chooses the mode automatically inside `index_repository`.
- Result field `index_mode` can be `full`, `incremental`, or `no_change`.
- Result field `fallback_reason` may explain why a full reindex replaced an incremental attempt.
- One cache DB is kept per repo path, not per branch.

Re-index when:

- First using CBM on a repo.
- A tool reports `project not indexed` or equivalent.
- After `git pull`, checkout, merge, or branch switch when hooks are absent.
- The user asks for the current graph.
- `detect_changes` needs current changed-file symbols rather than impact from the existing index.

The C# port has no background watcher or auto-index loop. Optional hooks such as `post-merge` and `post-checkout` can call `index_repository`, but this skill documents workflows only.

## Port Limitations

- C# only.
- No `semantic_query`; use BM25 `query` or regex filters instead.
- No cross-service graph: no `Route` nodes, no `HTTP_CALLS`, and `trace_path` `cross_service` is a no-op.
- `CALLS` edges exist only between indexed symbols; NuGet and other external calls may be missing.
- `detect_changes` uses the existing index; symbols in changed files may be stale until re-index.
- `.sln` and `.csproj` use Roslyn semantic resolution. Loose `.cs` files have reduced cross-file resolution.

## Error Recovery

- `project not indexed`: call `index_repository` with the repo path, then save the returned `project`.
- Ambiguous symbol or suffix: rerun `search_graph` with tighter `label`, `name_pattern`, `qn_pattern`, or `file_pattern`.
- `semantic_query` rejected: use `search_graph.query` for BM25 or regex filters like `name_pattern`, `qn_pattern`, and `file_pattern`.
- Empty or surprising call graph: check whether the repo was indexed from a `.sln` or `.csproj`; loose files have weaker semantics.

## Additional Resources

- Full parameter reference: [reference.md](reference.md)
- Canonical repo docs: [tools.md](../../tools.md)
