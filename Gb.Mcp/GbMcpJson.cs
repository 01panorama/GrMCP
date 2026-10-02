using System.Text.Json;
using System.Text.Json.Serialization;
using Gb.Graph;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Mcp;

internal static class GbMcpJson
{
    private const int CompactNestedListLimit = 8;

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    internal static string FormatError(string error, string? hint = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["error"] = error,
        };

        if (!string.IsNullOrWhiteSpace(hint))
        {
            payload["hint"] = hint;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatIndexResult(IndexRepositoryResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                project = result.ProjectName,
                status = "indexed",
                nodes = result.NodeCount,
                edges = result.EdgeCount,
                root_path = result.RepositoryRoot,
                index_mode = result.Mode switch
                {
                    IndexMode.Incremental => "incremental",
                    IndexMode.NoChange => "no_change",
                    _ => "full",
                },
                fallback_reason = result.FallbackReason,
                file_hashes = new
                {
                    unchanged = result.FileChanges.Unchanged,
                    changed = result.FileChanges.Changed,
                    @new = result.FileChanges.New,
                    deleted = result.FileChanges.Deleted,
                    mode_skipped = result.FileChanges.ModeSkipped,
                    total = result.FileChanges.Total,
                },
            },
            Options);
    }

    internal static string FormatIndexStatus(GbIndexStatus status)
    {
        var payload = new Dictionary<string, object?>
        {
            ["project"] = status.Project,
            ["nodes"] = status.Nodes,
            ["edges"] = status.Edges,
            ["status"] = status.Status,
            ["root_path"] = status.RootPath,
        };

        if (status.Nodes == 0 && status.Status != "not_found")
        {
            payload["hint"] =
                "Project is empty. Re-run index_repository(repo_path=...) to populate.";
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatListProjects(IReadOnlyList<GbCachedProject> projects)
    {
        var payload = new Dictionary<string, object?>
        {
            ["projects"] = projects.Select(project => new
            {
                name = project.Name,
                root_path = project.RootPath,
                nodes = project.NodeCount,
                edges = project.EdgeCount,
                size_bytes = project.SizeBytes,
            }).ToArray(),
        };

        if (projects.Count == 0)
        {
            payload["hint"] = "No projects indexed. Call index_repository(repo_path=...) first.";
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatListTools(IReadOnlyList<GbToolDefinition> tools)
    {
        return JsonSerializer.Serialize(
            new
            {
                total = tools.Count,
                tools = tools.Select(tool => new
                {
                    tool.Name,
                    tool.Category,
                    tool.Description,
                    Parameters = tool.Parameters.Select(parameter => new
                    {
                        parameter.Name,
                        parameter.Type,
                        parameter.Required,
                        parameter.Description,
                    }).ToArray(),
                    tool.Usage,
                    ExampleInput = ParseCatalogJson(tool.ExampleInput),
                    ExampleOutput = ParseCatalogJson(tool.ExampleOutput),
                    tool.CodeSnippet,
                    Caveats = tool.Caveats,
                }).ToArray(),
            },
            Options);
    }

    internal static string FormatDeleteProject(string project, bool deleted)
    {
        return JsonSerializer.Serialize(
            new
            {
                project,
                status = deleted ? "deleted" : "not_found",
            },
            Options);
    }

    private static readonly string[] GraphSchemaNodeColumns =
    [
        "name",
        "qualified_name",
        "label",
        "file_path",
        "start_line",
        "end_line",
    ];

    private static readonly string[] GraphSchemaPropertyTypeOrder =
    [
        "integer",
        "real",
        "boolean",
        "text",
        "mixed",
    ];

    internal static string FormatGraphSchema(GbGraphSchemaResponse schema)
    {
        var counts = schema.Counts;
        var catalog = schema.Properties;
        if (!SchemaPropertyCatalogEnabled() || catalog.Count == 0)
        {
            return JsonSerializer.Serialize(
                new
                {
                    node_labels = counts.NodeLabels.Select(label => new
                    {
                        label = label.Label,
                        count = label.Count,
                        properties = Array.Empty<string>(),
                    }),
                    edge_types = counts.EdgeTypes.Select(edge => new
                    {
                        type = edge.Type,
                        count = edge.Count,
                        properties = Array.Empty<string>(),
                    }),
                },
                Options);
        }

        var nodeCatalog = catalog.Where(row => row.Owner == "node").ToLookup(row => row.Kind);
        var edgeCatalog = catalog.Where(row => row.Owner == "edge").ToLookup(row => row.Kind);
        var nodeCanonical = BuildSchemaPropertyCanonical(
            counts.NodeLabels.Select(label => label.Label),
            nodeCatalog);
        var edgeCanonical = BuildSchemaPropertyCanonical(
            counts.EdgeTypes.Select(edge => edge.Type),
            edgeCatalog);

        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["columns"] = GraphSchemaNodeColumns,
                ["node_labels"] = counts.NodeLabels
                    .Select(label => FormatSchemaNodeLabel(label, nodeCatalog, nodeCanonical))
                    .ToArray(),
                ["edge_types"] = counts.EdgeTypes
                    .Select(edge => FormatSchemaEdgeType(edge, edgeCatalog, edgeCanonical))
                    .ToArray(),
            },
            Options);
    }

    internal static bool SchemaPropertyCatalogEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("GB_SCHEMA_PROPERTIES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        raw = raw.Trim();
        return !raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !raw.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildSchemaPropertyCanonical(
        IEnumerable<string> kindsInOrder,
        ILookup<string, GbSchemaProperty> catalog)
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in kindsInOrder)
        {
            var properties = catalog[kind].ToList();
            if (properties.Count == 0)
            {
                continue;
            }

            var signature = BuildSchemaPropertySignature(properties);
            if (!canonical.ContainsKey(signature))
            {
                canonical[signature] = kind;
            }
        }

        return canonical;
    }

    private static string BuildSchemaPropertySignature(IReadOnlyList<GbSchemaProperty> properties)
    {
        return string.Join(
            "\n",
            properties
                .OrderBy(property => property.ValueType, StringComparer.Ordinal)
                .ThenBy(property => property.PropertyKey, StringComparer.Ordinal)
                .Select(property => property.ValueType + "\0" + property.PropertyKey));
    }

    private static Dictionary<string, object?> FormatSchemaNodeLabel(
        GbLabelSchema label,
        ILookup<string, GbSchemaProperty> catalog,
        IReadOnlyDictionary<string, string> canonicalBySignature)
    {
        return FormatSchemaKindEntry(
            label.Label,
            label.Count,
            catalog[label.Label].ToList(),
            canonicalBySignature,
            isEdge: false);
    }

    private static Dictionary<string, object?> FormatSchemaEdgeType(
        GbEdgeTypeSchema edge,
        ILookup<string, GbSchemaProperty> catalog,
        IReadOnlyDictionary<string, string> canonicalBySignature)
    {
        return FormatSchemaKindEntry(
            edge.Type,
            edge.Count,
            catalog[edge.Type].ToList(),
            canonicalBySignature,
            isEdge: true);
    }

    private static Dictionary<string, object?> FormatSchemaKindEntry(
        string kind,
        int count,
        IReadOnlyList<GbSchemaProperty> properties,
        IReadOnlyDictionary<string, string> canonicalBySignature,
        bool isEdge)
    {
        var entry = new Dictionary<string, object?>
        {
            [isEdge ? "type" : "label"] = kind,
            ["count"] = count,
        };

        if (properties.Count == 0)
        {
            return entry;
        }

        var signature = BuildSchemaPropertySignature(properties);
        if (canonicalBySignature.TryGetValue(signature, out var canonicalKind)
            && !string.Equals(canonicalKind, kind, StringComparison.Ordinal))
        {
            entry["properties_same_as"] = canonicalKind;
            return entry;
        }

        entry["properties"] = GroupSchemaPropertiesByType(properties);
        return entry;
    }

    private static Dictionary<string, string[]> GroupSchemaPropertiesByType(
        IReadOnlyList<GbSchemaProperty> properties)
    {
        var grouped = properties
            .GroupBy(property => property.ValueType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(property => property.PropertyKey)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var ordered = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var valueType in GraphSchemaPropertyTypeOrder)
        {
            if (grouped.Remove(valueType, out var keys))
            {
                ordered[valueType] = keys;
            }
        }

        foreach (var pair in grouped.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ordered[pair.Key] = pair.Value;
        }

        return ordered;
    }

    internal static string FormatQueryGraph(GbCypherQueryResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                columns = result.Columns,
                rows = result.Rows,
                total = result.Rows.Count,
                hint = result.Hint,
            },
            Options);
    }

    internal static string FormatArchitecture(
        GbArchitectureResult result,
        GbVerbosity verbosity = GbVerbosity.Full)
    {
        var payload = new Dictionary<string, object?>
        {
            ["project"] = result.Project,
            ["total_nodes"] = result.TotalNodes,
            ["total_edges"] = result.TotalEdges,
        };

        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            payload["path"] = result.Path;
            payload["root_total_nodes"] = result.RootTotalNodes;
            payload["root_total_edges"] = result.RootTotalEdges;
        }

        if (result.Structure?.NodeLabels is { Count: > 0 } nodeLabels)
        {
            payload["node_labels"] = nodeLabels.Select(label => new
            {
                label = label.Label,
                count = label.Count,
            }).ToArray();
        }

        if (result.Dependencies?.EdgeTypes is { Count: > 0 } edgeTypes)
        {
            payload["edge_types"] = edgeTypes.Select(edge => new
            {
                type = edge.Type,
                count = edge.Count,
            }).ToArray();
        }

        if (result.Languages is { Count: > 0 } languages)
        {
            payload["languages"] = languages.Select(lang => new
            {
                language = lang.Language,
                file_count = lang.FileCount,
            }).ToArray();
        }

        if (result.Packages is { Count: > 0 } packages)
        {
            payload["packages"] = packages.Select(pkg => new
            {
                name = pkg.Name,
                node_count = pkg.NodeCount,
                fan_in = pkg.FanIn,
                fan_out = pkg.FanOut,
            }).ToArray();
        }

        if (result.EntryPoints is { Count: > 0 } entryPoints)
        {
            payload["entry_points"] = entryPoints.Select(ep => new
            {
                name = ep.Name,
                qualified_name = ep.QualifiedName,
                file = ep.File,
            }).ToArray();
        }

        if (result.Hotspots is { Count: > 0 } hotspots)
        {
            payload["hotspots"] = hotspots.Select(hotspot => new
            {
                name = hotspot.Name,
                qualified_name = hotspot.QualifiedName,
                fan_in = hotspot.FanIn,
            }).ToArray();
        }

        if (result.Boundaries is { Count: > 0 } boundaries)
        {
            payload["boundaries"] = boundaries.Select(boundary => new
            {
                from = boundary.From,
                to = boundary.To,
                call_count = boundary.CallCount,
            }).ToArray();
        }

        if (result.Layers is { Count: > 0 } layers)
        {
            payload["layers"] = layers.Select(layer => new
            {
                name = layer.Name,
                layer = layer.Layer,
                reason = layer.Reason,
            }).ToArray();
        }

        if (result.Clusters is { Count: > 0 } clusters)
        {
            payload["clusters"] = clusters.Select(cluster => new
            {
                id = cluster.Id,
                label = cluster.Label,
                members = cluster.Members,
                cohesion = cluster.Cohesion,
                top_nodes = LimitList(cluster.TopNodes, verbosity),
                packages = LimitList(cluster.Packages, verbosity),
                edge_types = LimitList(cluster.EdgeTypes, verbosity),
            }).ToArray();
        }

        if (result.FileTree is { Count: > 0 } fileTree)
        {
            payload["file_tree"] = LimitList(fileTree, verbosity).Select(entry => new
            {
                path = entry.Path,
                type = entry.Type,
                children = entry.Children,
            }).ToArray();
        }

        if (result.Runtime is not null)
        {
            payload["runtime"] = new Dictionary<string, object?>
            {
                ["total_observations"] = result.Runtime.TotalObservations,
                ["matched_edges"] = result.Runtime.MatchedEdges,
                ["observations"] = result.Runtime.Observations.Select(observation => new
                {
                    caller = observation.Caller,
                    callee = observation.Callee,
                    service = observation.Service,
                    target_service = observation.TargetService,
                    route = observation.Route,
                    method = observation.Method,
                    count = observation.Count,
                    error_count = observation.ErrorCount,
                    avg_duration_ms = observation.AvgDurationMs,
                    p99_duration_ms = observation.P99DurationMs,
                    matched = observation.Matched,
                }).ToArray(),
            };
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatTracePath(GbTracePathResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["function"] = result.FunctionName,
            ["direction"] = result.Direction,
        };

        if (!string.IsNullOrWhiteSpace(result.Mode))
        {
            payload["mode"] = result.Mode;
        }

        if (result.Callers is not null)
        {
            payload["callers"] = FormatTraceHops(result.Callers);
        }

        if (result.Callees is not null)
        {
            payload["callees"] = FormatTraceHops(result.Callees);
        }

        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            payload["note"] = result.Note;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatTracePathAmbiguous(GbTracePathResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                status = "ambiguous",
                message =
                    $"{result.Suggestions!.Count} matches for \"{result.FunctionName}\". "
                    + "Pick a qualified_name from suggestions below, or use "
                    + "search_graph(name_pattern=\"...\") to narrow results.",
                suggestions = result.Suggestions.Select(name => new
                {
                    qualified_name = name,
                }),
            },
            Options);
    }

    internal static string FormatTracePathNotFound(GbTracePathResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                error = "function not found",
                function_name = result.FunctionName,
                hint = result.Error,
            },
            Options);
    }

    private static object[] FormatTraceHops(IReadOnlyList<GbTraceHop> hops)
    {
        return hops.Select(hop =>
        {
            var item = new Dictionary<string, object?>
            {
                ["name"] = hop.Name,
                ["qualified_name"] = hop.QualifiedName,
                ["hop"] = hop.Hop,
            };

            if (!string.IsNullOrWhiteSpace(hop.Risk))
            {
                item["risk"] = hop.Risk;
            }

            if (hop.IsTest == true)
            {
                item["is_test"] = true;
            }

            return item;
        }).ToArray();
    }

    internal static string FormatSearchGraph(
        string projectName,
        GbSearchGraphResult search,
        string? searchMode = null,
        GbVerbosity verbosity = GbVerbosity.Full)
    {
        using var store = GbCachePaths.OpenProjectStore(projectName);
        var degrees = search.Results.Count == 0
            ? new Dictionary<long, GbNodeDegree>()
            : store.BatchCountDegrees(search.Results.Select(node => node.Id).ToArray());

        var payload = new Dictionary<string, object?>
        {
            ["total"] = search.Total,
            ["has_more"] = search.HasMore,
            ["results"] = search.Results.Select(node =>
            {
                degrees.TryGetValue(node.Id, out var degree);
                return BuildSearchResultItem(node, degree ?? new GbNodeDegree(0, 0), verbosity);
            }).ToArray(),
        };

        if (!string.IsNullOrWhiteSpace(searchMode))
        {
            payload["search_mode"] = searchMode;
        }

        if (search.Total == 0)
        {
            payload["hint"] =
                "No nodes match this query. Try removing filters or broadening name_pattern/qn_pattern.";
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatCodeSnippetNotFound(GbCodeSnippetResult snippet)
    {
        if (string.Equals(snippet.MatchType, "ambiguous", StringComparison.Ordinal)
            && snippet.Suggestions is { Count: > 0 })
        {
            return JsonSerializer.Serialize(
                new
                {
                    status = "ambiguous",
                    message =
                        $"{snippet.Suggestions.Count} matches for \"{snippet.QualifiedName}\". "
                        + "Pick a qualified_name from suggestions below, or use "
                        + "search_graph(name_pattern=\"...\") to narrow results.",
                    suggestions = snippet.Suggestions.Select(name => new
                    {
                        qualified_name = name,
                    }),
                },
                Options);
        }

        return JsonSerializer.Serialize(
            new
            {
                error = snippet.Error ?? "symbol not found",
                hint =
                    "Use search_graph(name_pattern=\"...\") first to discover the exact "
                    + "qualified_name, then pass it to get_code_snippet.",
            },
            Options);
    }

    internal static string FormatCodeSnippet(
        GbCodeSnippetResult snippet,
        GbNode node,
        bool includeNeighbors,
        GbStore store,
        GbVerbosity verbosity = GbVerbosity.Full)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = node.Name,
            ["qualified_name"] = node.QualifiedName,
            ["label"] = node.Label,
            ["file_path"] = snippet.FilePath,
            ["start_line"] = snippet.StartLine,
            ["end_line"] = snippet.EndLine,
            ["source"] = snippet.Code ?? "(source not available)",
        };

        if (!string.IsNullOrWhiteSpace(snippet.MatchType)
            && !string.Equals(snippet.MatchType, "exact", StringComparison.Ordinal))
        {
            payload["match_method"] = snippet.MatchType;
        }

        if (verbosity == GbVerbosity.Full)
        {
            MergeProperties(payload, node.PropertiesJson);
        }

        var degree = store.GetNodeDegree(node.Id);
        payload["callers"] = degree.InDegree;
        payload["callees"] = degree.OutDegree;

        if (includeNeighbors)
        {
            var neighbors = store.GetNodeNeighborNames(node.Id, limit: 20);
            if (neighbors.Callers.Count > 0)
            {
                payload["caller_names"] = neighbors.Callers;
            }

            if (neighbors.Callees.Count > 0)
            {
                payload["callee_names"] = neighbors.Callees;
            }
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatSearchCode(
        GbSearchCodeResult result,
        GbVerbosity verbosity = GbVerbosity.Full)
    {
        var payload = new Dictionary<string, object?>();

        if (result.Files is not null)
        {
            payload["files"] = result.Files;
        }
        else
        {
            payload["results"] = result.Results.Select(hit =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["node"] = hit.Node,
                    ["qualified_name"] = hit.QualifiedName,
                    ["label"] = hit.Label,
                    ["file"] = hit.File,
                    ["start_line"] = hit.StartLine,
                    ["end_line"] = hit.EndLine,
                    ["in_degree"] = hit.InDegree,
                    ["out_degree"] = hit.OutDegree,
                    ["match_lines"] = hit.MatchLines,
                };

                if (!string.IsNullOrWhiteSpace(hit.Source))
                {
                    item["source"] = hit.Source;
                }

                if (!string.IsNullOrWhiteSpace(hit.Context))
                {
                    item["context"] = hit.Context;
                    item["context_start"] = hit.ContextStart;
                }

                return item;
            }).ToArray();

            if (verbosity == GbVerbosity.Full)
            {
                payload["raw_matches"] = result.RawMatches.Select(raw => new
                {
                    file = raw.File,
                    line = raw.Line,
                    content = raw.Content,
                }).ToArray();
            }
        }

        payload["directories"] = result.Directories;
        if (verbosity == GbVerbosity.Full)
        {
            payload["total_grep_matches"] = result.TotalGrepMatches;
        }

        payload["total_results"] = result.TotalResults;
        payload["raw_match_count"] = result.RawMatchCount;
        payload["elapsed_ms"] = result.ElapsedMs;

        if (verbosity == GbVerbosity.Full && !string.IsNullOrWhiteSpace(result.DedupRatio))
        {
            payload["dedup_ratio"] = result.DedupRatio;
        }

        if (result.Warnings.Count > 0)
        {
            payload["warnings"] = result.Warnings;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatManageAdr(GbManageAdrResult result)
    {
        var payload = new Dictionary<string, object?>();

        if (result.Sections is not null)
        {
            payload["sections"] = result.Sections;
        }
        else if (result.Content is not null)
        {
            payload["content"] = result.Content;
        }

        if (!string.IsNullOrEmpty(result.Status))
        {
            payload["status"] = result.Status;
        }

        if (!string.IsNullOrEmpty(result.AdrHint))
        {
            payload["adr_hint"] = result.AdrHint;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatIngestTraces(GbIngestTracesResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = result.Status,
            ["traces_received"] = result.TracesReceived,
            ["traces_ingested"] = result.TracesIngested,
            ["edges_matched"] = result.EdgesMatched,
            ["unresolved"] = result.Unresolved,
        };

        if (result.Warnings.Count > 0)
        {
            payload["warnings"] = result.Warnings;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    internal static string FormatDetectChanges(GbDetectChangesResult result)
    {
        if (!result.Success)
        {
            var errorPayload = new Dictionary<string, object?>
            {
                ["error"] = result.ErrorCode,
            };

            if (!string.IsNullOrWhiteSpace(result.Hint))
            {
                errorPayload["hint"] = result.Hint;
            }

            return JsonSerializer.Serialize(errorPayload, Options);
        }

        var payload = new Dictionary<string, object?>
        {
            ["changed_files"] = result.ChangedFiles,
            ["changed_count"] = result.ChangedCount,
            ["depth"] = result.Depth,
            ["base"] = result.Base,
            ["scope"] = result.Scope,
        };

        if (!string.IsNullOrWhiteSpace(result.Head))
        {
            payload["head"] = result.Head;
        }

        if (!string.IsNullOrWhiteSpace(result.Branch))
        {
            payload["branch"] = result.Branch;
        }

        if (!string.Equals(result.Scope, "files", StringComparison.Ordinal))
        {
            payload["changed_symbols"] = result.ChangedSymbols.Select(BuildImpactedSymbolItem).ToArray();
            payload["changed_symbol_count"] = result.ChangedSymbolCount;
        }

        if (string.Equals(result.Scope, "impact", StringComparison.Ordinal))
        {
            payload["impacted_symbols"] = result.ImpactedSymbols.Select(BuildImpactedSymbolItem).ToArray();
            payload["impacted_symbol_count"] = result.ImpactedSymbolCount;
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    private static Dictionary<string, object?> BuildImpactedSymbolItem(GbImpactedSymbol symbol)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = symbol.Name,
            ["qualified_name"] = symbol.QualifiedName,
            ["label"] = symbol.Label,
            ["file"] = symbol.File,
            ["hop"] = symbol.Hop,
            ["direction"] = symbol.Direction,
        };
    }

    private static JsonElement ParseCatalogJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Dictionary<string, object?> BuildSearchResultItem(
        GbNode node,
        GbNodeDegree degree,
        GbVerbosity verbosity)
    {
        var item = new Dictionary<string, object?>
        {
            ["name"] = node.Name,
            ["qualified_name"] = node.QualifiedName,
            ["label"] = node.Label,
            ["file_path"] = node.FilePath,
            ["start_line"] = node.StartLine,
            ["end_line"] = node.EndLine,
            ["in_degree"] = degree.InDegree,
            ["out_degree"] = degree.OutDegree,
        };

        if (verbosity == GbVerbosity.Full)
        {
            MergeProperties(item, node.PropertiesJson);
        }

        return item;
    }

    private static IReadOnlyList<T> LimitList<T>(IReadOnlyList<T> items, GbVerbosity verbosity)
    {
        if (verbosity != GbVerbosity.Compact || items.Count <= CompactNestedListLimit)
        {
            return items;
        }

        return items.Take(CompactNestedListLimit).ToArray();
    }

    private static void MergeProperties(IDictionary<string, object?> target, string propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(propertiesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            target[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.GetRawText(),
            };
        }
    }
}
