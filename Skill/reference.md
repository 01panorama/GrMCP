# CBM MCP Reference

Distilled from the canonical repo docs in [tools.md](../../../tools.md). Use `list_tools` for live server docs when behavior or examples need confirmation.

## Tool Index

| Category | Tools |
| --- | --- |
| lifecycle | `list_projects`, `index_repository`, `index_status`, `delete_project` |
| query | `search_graph`, `get_code_snippet`, `search_code`, `query_graph`, `get_graph_schema`, `get_architecture`, `trace_path` |
| mutation | `detect_changes`, `manage_adr`, `ingest_traces` |
| meta | `list_tools` |

## Index Result Shape

`index_repository` decides the `index_mode` automatically: `full`, `incremental`, or `no_change`. A `fallback_reason` may appear when an attempted incremental index falls back to full.

```json
{
  "project": "Users-example-MyApp",
  "status": "indexed",
  "nodes": 128,
  "edges": 256,
  "root_path": "/Users/example/MyApp",
  "index_mode": "incremental",
  "fallback_reason": null
}
```

## Lifecycle Tools

| Tool | Required params | Example input | Caveat |
| --- | --- | --- | --- |
| `list_projects` | none | `{}` | Returns a hint when no projects are indexed. |
| `index_repository` | `repo_path` | `{"repo_path":"/Users/example/MyApp"}` | Requires a C# project, solution, or loose C# files. `mode` exists for compatibility; do not model incremental as a separate workflow. |
| `index_status` | `project` | `{"project":"Users-example-MyApp"}` | Missing cache DB returns project-not-found guidance. |
| `delete_project` | `project` | `{"project":"Users-example-MyApp"}` | Deletes only the local CBM cache DB, not source files. |

## Query Tools

| Tool | Required params | Example input | Caveat |
| --- | --- | --- | --- |
| `search_graph` | `project` | `{"project":"Users-example-MyApp","label":"Method","name_pattern":"Execute","limit":5}` | `semantic_query` is unsupported in the C# port. |
| `get_code_snippet` | `project`, `qualified_name` | `{"project":"Users-example-MyApp","qualified_name":"Sample.Worker.Execute","include_neighbors":true}` | Ambiguous suffix matches return suggestions instead of source. |
| `search_code` | `project`, `pattern` | `{"project":"Users-example-MyApp","pattern":"Target","mode":"compact"}` | Implemented as pure .NET scanning, not shell grep. |
| `query_graph` | `project`, `query` | `{"project":"Users-example-MyApp","query":"MATCH (n:Method) RETURN n.name LIMIT 10"}` | Only the supported read-only Cypher subset is accepted. |
| `get_graph_schema` | `project` | `{"project":"Users-example-MyApp"}` | Property lists are placeholders in this C# port. |
| `get_architecture` | `project` | `{"project":"Users-example-MyApp","aspects":["all"]}` | Clustering runs on the `CALLS` graph only; runtime data appears only after `ingest_traces`. |
| `trace_path` | `project`, `function_name` | `{"project":"Users-example-MyApp","function_name":"Target","direction":"inbound","depth":3}` | `cross_service` is a no-op because Route nodes are out of scope. |

## Mutation Tools

| Tool | Required params | Example input | Caveat |
| --- | --- | --- | --- |
| `detect_changes` | `project` | `{"project":"Users-example-MyApp","scope":"impact","depth":2,"base_branch":"main"}` | Non-git repos return `not_a_git_repo`; changed-file symbols may be stale until re-index. |
| `manage_adr` | `project` | `{"project":"Users-example-MyApp","mode":"update","content":"## PURPOSE\nDocument key decisions.\n"}` | No delete mode; `sections` is accepted but ignored by the handler. |
| `ingest_traces` | `project`, `traces` | `{"project":"Users-example-MyApp","traces":[{"caller":"Run","callee":"Target","duration_ms":8.0,"count":1}]}` | Route observations do not create cross-service graph completeness. |

## Meta Tool

| Tool | Required params | Example input | Caveat |
| --- | --- | --- | --- |
| `list_tools` | none | `{"format":"markdown","tools":["search_graph","get_code_snippet"]}` | Use filters (`name`, `tools`, `category`) to avoid loading the full catalog. |

## Common Optional Params

| Tool | Useful optional params |
| --- | --- |
| `search_graph` | `query`, `label`, `name_pattern`, `qn_pattern`, `file_pattern`, `case_sensitive`, `limit`, `offset` |
| `get_code_snippet` | `include_neighbors` |
| `search_code` | `file_pattern`, `path_filter`, `mode`, `context`, `regex`, `limit` |
| `query_graph` | `max_rows` |
| `get_architecture` | `path`, `aspects` |
| `trace_path` | `direction`, `depth`, `mode`, `risk_labels`, `include_tests`, `edge_types` |
| `detect_changes` | `scope`, `depth`, `base_branch`, `since` |
| `manage_adr` | `mode`, `content`, `sections` |

## Cypher Examples

Dead code candidates:

```cypher
MATCH (m:Method)
WHERE NOT EXISTS {(m)<-[:CALLS]-()}
RETURN m.qualified_name
ORDER BY m.qualified_name
LIMIT 50
```

Callers of a target:

```cypher
MATCH (caller:Method)-[:CALLS]->(target:Method)
WHERE target.qualified_name ENDS WITH "Sample.Callee.Target"
RETURN caller.qualified_name, target.qualified_name
LIMIT 50
```

Hotspots by inbound calls:

```cypher
MATCH (caller:Method)-[:CALLS]->(target:Method)
RETURN target.qualified_name, count(caller) AS callers
ORDER BY callers DESC
LIMIT 20
```

## Qualified Name Tips

- Search first, then pass the returned `qualified_name` to snippet or trace tools.
- Suffix matching is useful only when the suffix is unique.
- If a tool returns suggestions, use one of them or rerun `search_graph` with tighter filters.
