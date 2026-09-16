using System.Text;

namespace Cbm.Mcp;

/// <summary>
/// Renders the distilled CBM MCP skill reference (Skill/reference.md).
/// Mechanical sections (tool index, verbosity list, optional params) are derived
/// from <see cref="CbmToolCatalog"/> so they cannot drift; editorial sections
/// (examples, caveats, Cypher, tips) are kept as static blocks.
/// </summary>
public static class CbmSkillReference
{
    private static readonly IReadOnlySet<string> OptionalParamCategories =
        new HashSet<string>(StringComparer.Ordinal) { "query", "mutation" };

    // Parameters accepted only for CBM parity; excluded from the curated optional-params list.
    private static readonly IReadOnlySet<string> CompatibilityOnlyParameters =
        new HashSet<string>(StringComparer.Ordinal) { "semantic_query", "parameter_name" };

    public static string RenderMarkdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine("# CBM MCP Reference");
        builder.AppendLine();
        builder.AppendLine("Distilled from the canonical repo docs in [tools.md](../../../tools.md). Use `list_tools` for live server docs when behavior or examples need confirmation.");
        builder.AppendLine();

        AppendToolIndex(builder);
        AppendIndexResultShape(builder);
        AppendLifecycleTools(builder);
        AppendQueryTools(builder);
        AppendMutationTools(builder);
        AppendMetaTools(builder);
        AppendOutputVerbosity(builder);
        AppendCommonOptionalParams(builder);
        AppendCypherExamples(builder);
        AppendQualifiedNameTips(builder);

        // Normalize to LF so emitted docs are byte-identical on macOS and Windows.
        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendToolIndex(StringBuilder builder)
    {
        builder.AppendLine("## Tool Index");
        builder.AppendLine();
        builder.AppendLine("| Category | Tools |");
        builder.AppendLine("| --- | --- |");
        foreach (var category in CbmToolCatalog.Categories)
        {
            var names = string.Join(", ", CbmToolCatalog.Tools
                .Where(tool => string.Equals(tool.Category, category, StringComparison.Ordinal))
                .Select(tool => $"`{tool.Name}`"));
            builder.AppendLine($"| {category} | {names} |");
        }

        builder.AppendLine();
    }

    private static void AppendIndexResultShape(StringBuilder builder)
    {
        builder.AppendLine("## Index Result Shape");
        builder.AppendLine();
        builder.AppendLine("`index_repository` decides the `index_mode` automatically: `full`, `incremental`, or `no_change`. A `fallback_reason` may appear when an attempted incremental index falls back to full.");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"project\": \"Users-example-MyApp\",");
        builder.AppendLine("  \"status\": \"indexed\",");
        builder.AppendLine("  \"nodes\": 128,");
        builder.AppendLine("  \"edges\": 256,");
        builder.AppendLine("  \"root_path\": \"/Users/example/MyApp\",");
        builder.AppendLine("  \"index_mode\": \"incremental\",");
        builder.AppendLine("  \"fallback_reason\": null");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static void AppendLifecycleTools(StringBuilder builder)
    {
        builder.AppendLine("## Lifecycle Tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Required params | Example input | Caveat |");
        builder.AppendLine("| --- | --- | --- | --- |");
        builder.AppendLine("| `list_projects` | none | `{}` | Returns a hint when no projects are indexed. |");
        builder.AppendLine("| `index_repository` | `repo_path` | `{\"repo_path\":\"/Users/example/MyApp\"}` | Requires a C# project, solution, or loose C# files. `mode` exists for compatibility; do not model incremental as a separate workflow. |");
        builder.AppendLine("| `index_status` | `project` | `{\"project\":\"Users-example-MyApp\"}` | Missing cache DB returns project-not-found guidance. |");
        builder.AppendLine("| `delete_project` | `project` | `{\"project\":\"Users-example-MyApp\"}` | Deletes only the local CBM cache DB, not source files. |");
        builder.AppendLine();
    }

    private static void AppendQueryTools(StringBuilder builder)
    {
        builder.AppendLine("## Query Tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Required params | Example input | Caveat |");
        builder.AppendLine("| --- | --- | --- | --- |");
        builder.AppendLine("| `search_graph` | `project` | `{\"project\":\"Users-example-MyApp\",\"label\":\"Method\",\"name_pattern\":\"Execute\",\"limit\":5}` | `semantic_query` is unsupported in the C# port. |");
        builder.AppendLine("| `get_code_snippet` | `project`, `qualified_name` | `{\"project\":\"Users-example-MyApp\",\"qualified_name\":\"Sample.Worker.Execute\",\"include_neighbors\":true}` | Ambiguous suffix matches return suggestions instead of source. |");
        builder.AppendLine("| `search_code` | `project`, `pattern` | `{\"project\":\"Users-example-MyApp\",\"pattern\":\"Target\",\"mode\":\"compact\"}` | Implemented as pure .NET scanning, not shell grep. |");
        builder.AppendLine("| `query_graph` | `project`, `query` | `{\"project\":\"Users-example-MyApp\",\"query\":\"MATCH (n:Method) RETURN n.name LIMIT 10\"}` | Only the supported read-only Cypher subset is accepted. |");
        builder.AppendLine("| `get_graph_schema` | `project` | `{\"project\":\"Users-example-MyApp\"}` | Property lists are placeholders in this C# port. |");
        builder.AppendLine("| `get_architecture` | `project` | `{\"project\":\"Users-example-MyApp\",\"aspects\":[\"all\"]}` | Clustering runs on the `CALLS` graph only; runtime data appears only after `ingest_traces`. |");
        builder.AppendLine("| `trace_path` | `project`, `function_name` | `{\"project\":\"Users-example-MyApp\",\"function_name\":\"Target\",\"direction\":\"inbound\",\"depth\":3}` | `cross_service` is a no-op because Route nodes are out of scope. |");
        builder.AppendLine();
    }

    private static void AppendMutationTools(StringBuilder builder)
    {
        builder.AppendLine("## Mutation Tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Required params | Example input | Caveat |");
        builder.AppendLine("| --- | --- | --- | --- |");
        builder.AppendLine("| `detect_changes` | `project` | `{\"project\":\"Users-example-MyApp\",\"scope\":\"impact\",\"depth\":2,\"base_branch\":\"main\"}` | Non-git repos return `not_a_git_repo`; changed-file symbols may be stale until re-index. |");
        builder.AppendLine("| `manage_adr` | `project` | `{\"project\":\"Users-example-MyApp\",\"mode\":\"update\",\"content\":\"## PURPOSE\\nDocument key decisions.\\n\"}` | No delete mode; `sections` is accepted but ignored by the handler. |");
        builder.AppendLine("| `ingest_traces` | `project`, `traces` | `{\"project\":\"Users-example-MyApp\",\"traces\":[{\"caller\":\"Run\",\"callee\":\"Target\",\"duration_ms\":8.0,\"count\":1}]}` | Route observations do not create cross-service graph completeness. |");
        builder.AppendLine();
    }

    private static void AppendMetaTools(StringBuilder builder)
    {
        builder.AppendLine("## Meta Tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Required params | Example input | Caveat |");
        builder.AppendLine("| --- | --- | --- | --- |");
        builder.AppendLine("| `list_tools` | none | `{\"format\":\"markdown\",\"tools\":[\"search_graph\",\"get_code_snippet\"]}` | Use filters (`name`, `tools`, `category`) to avoid loading the full catalog. |");
        builder.AppendLine("| `get_skill_reference` | none | `{}` | Returns this distilled reference as markdown; use `list_tools` for full per-tool docs. |");
        builder.AppendLine();
    }

    private static void AppendOutputVerbosity(StringBuilder builder)
    {
        var verbosityTools = CbmToolCatalog.Tools
            .Where(tool => tool.Parameters.Any(parameter => parameter.Name == "verbosity"))
            .Select(tool => $"`{tool.Name}`")
            .ToArray();

        builder.AppendLine("## Output Verbosity");
        builder.AppendLine();
        builder.AppendLine(
            $"{JoinWithAnd(verbosityTools)} accept `verbosity`: `full` (default, CBM-parity payloads) or `compact` (fewer tokens). Choose once per call from the question type; do not call the same tool twice to switch modes.");
        builder.AppendLine();
    }

    private static void AppendCommonOptionalParams(StringBuilder builder)
    {
        builder.AppendLine("## Common Optional Params");
        builder.AppendLine();
        builder.AppendLine("| Tool | Useful optional params |");
        builder.AppendLine("| --- | --- |");
        foreach (var tool in CbmToolCatalog.Tools)
        {
            if (!OptionalParamCategories.Contains(tool.Category))
            {
                continue;
            }

            var optional = tool.Parameters
                .Where(parameter => !parameter.Required && !CompatibilityOnlyParameters.Contains(parameter.Name))
                .Select(parameter => $"`{parameter.Name}`")
                .ToArray();
            if (optional.Length == 0)
            {
                continue;
            }

            builder.AppendLine($"| `{tool.Name}` | {string.Join(", ", optional)} |");
        }

        builder.AppendLine();
    }

    private static void AppendCypherExamples(StringBuilder builder)
    {
        builder.AppendLine("## Cypher Examples");
        builder.AppendLine();
        builder.AppendLine("Dead code candidates:");
        builder.AppendLine();
        builder.AppendLine("```cypher");
        builder.AppendLine("MATCH (m:Method)");
        builder.AppendLine("WHERE NOT EXISTS {(m)<-[:CALLS]-()}");
        builder.AppendLine("RETURN m.qualified_name");
        builder.AppendLine("ORDER BY m.qualified_name");
        builder.AppendLine("LIMIT 50");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Callers of a target:");
        builder.AppendLine();
        builder.AppendLine("```cypher");
        builder.AppendLine("MATCH (caller:Method)-[:CALLS]->(target:Method)");
        builder.AppendLine("WHERE target.qualified_name ENDS WITH \"Sample.Callee.Target\"");
        builder.AppendLine("RETURN caller.qualified_name, target.qualified_name");
        builder.AppendLine("LIMIT 50");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Hotspots by inbound calls:");
        builder.AppendLine();
        builder.AppendLine("```cypher");
        builder.AppendLine("MATCH (caller:Method)-[:CALLS]->(target:Method)");
        builder.AppendLine("RETURN target.qualified_name, count(caller) AS callers");
        builder.AppendLine("ORDER BY callers DESC");
        builder.AppendLine("LIMIT 20");
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static void AppendQualifiedNameTips(StringBuilder builder)
    {
        builder.AppendLine("## Qualified Name Tips");
        builder.AppendLine();
        builder.AppendLine("- Search first, then pass the returned `qualified_name` to snippet or trace tools.");
        builder.AppendLine("- Suffix matching is useful only when the suffix is unique.");
        builder.AppendLine("- If a tool returns suggestions, use one of them or rerun `search_graph` with tighter filters.");
    }

    private static string JoinWithAnd(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };
    }
}
