namespace Gb.Graph;

public sealed record GbNode
{
    public long Id { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record GbEdge
{
    public long Id { get; init; }
    public string Project { get; init; } = string.Empty;
    public long SourceId { get; init; }
    public long TargetId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record GbProject
{
    public string Name { get; init; } = string.Empty;
    public string IndexedAt { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
}

public sealed record GbFileHash
{
    public string Project { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long MtimeNs { get; init; }
    public long Size { get; init; }
}

public sealed record GbNodeDegree(int InDegree, int OutDegree);

public sealed record GbNodeNeighbors(
    IReadOnlyList<string> Callers,
    IReadOnlyList<string> Callees);

public sealed record GbGraphEdge
{
    public string Project { get; init; } = string.Empty;
    public string SourceQualifiedName { get; init; } = string.Empty;
    public string TargetQualifiedName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record GbDefinitionExtractionResult(
    IReadOnlyList<GbNode> Nodes,
    IReadOnlyList<GbGraphEdge> Edges);

public sealed record GbCachedProject(
    string Name,
    string IndexedAt,
    string RootPath,
    long SizeBytes,
    int NodeCount,
    int EdgeCount);

public sealed record GbIndexStatus(
    string Project,
    string RootPath,
    int Nodes,
    int Edges,
    string Status);

public sealed record GbLabelSchema(string Label, int Count);

public sealed record GbEdgeTypeSchema(string Type, int Count);

public sealed record GbGraphSchema(
    IReadOnlyList<GbLabelSchema> NodeLabels,
    IReadOnlyList<GbEdgeTypeSchema> EdgeTypes);

public sealed record GbSchemaProperty(
    string Owner,
    string Kind,
    string PropertyKey,
    string ValueType);

public sealed record GbGraphSchemaResponse(
    GbGraphSchema Counts,
    IReadOnlyList<GbSchemaProperty> Properties);

public sealed record GbSearchGraphResult(
    IReadOnlyList<GbNode> Results,
    int Total,
    int Offset,
    int Limit,
    bool HasMore);

public sealed record GbCodeSnippetResult(
    bool Found,
    string? QualifiedName,
    string? FilePath,
    int StartLine,
    int EndLine,
    string? Code,
    string? MatchType,
    IReadOnlyList<string>? Suggestions,
    string? Error);

public sealed record GbCypherQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Hint = null);

public sealed record GbLanguageCount(string Language, int FileCount);

public sealed record GbPackageSummary(
    string Name,
    int NodeCount,
    int FanIn,
    int FanOut);

public sealed record GbEntryPoint(
    string Name,
    string QualifiedName,
    string File);

public sealed record GbHotspot(
    string Name,
    string QualifiedName,
    int FanIn);

public sealed record GbCrossPackageBoundary(
    string From,
    string To,
    int CallCount);

public sealed record GbPackageLayer(
    string Name,
    string Layer,
    string Reason);

public sealed record GbClusterInfo(
    int Id,
    string Label,
    int Members,
    double Cohesion,
    IReadOnlyList<string> TopNodes,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> EdgeTypes);

public sealed record GbFileTreeEntry(
    string Path,
    string Type,
    int Children);

public sealed record GbArchitectureResult(
    string Project,
    string? Path,
    int TotalNodes,
    int TotalEdges,
    int? RootTotalNodes,
    int? RootTotalEdges,
    GbGraphSchema? Structure,
    GbGraphSchema? Dependencies,
    IReadOnlyList<GbLanguageCount>? Languages,
    IReadOnlyList<GbPackageSummary>? Packages,
    IReadOnlyList<GbEntryPoint>? EntryPoints,
    IReadOnlyList<GbHotspot>? Hotspots,
    IReadOnlyList<GbCrossPackageBoundary>? Boundaries,
    IReadOnlyList<GbPackageLayer>? Layers,
    IReadOnlyList<GbClusterInfo>? Clusters,
    IReadOnlyList<GbFileTreeEntry>? FileTree,
    GbRuntimeSummary? Runtime = null);

public sealed record GbRuntimeSummary(
    int TotalObservations,
    int MatchedEdges,
    IReadOnlyList<GbRuntimeObservation> Observations);

public sealed record GbRuntimeObservation(
    string Caller,
    string Callee,
    string Service,
    string TargetService,
    string Route,
    string Method,
    int Count,
    int ErrorCount,
    double AvgDurationMs,
    double P99DurationMs,
    bool Matched);

public sealed record GbNormalizedTraceEntry(
    string Caller,
    string Callee,
    string Service,
    string TargetService,
    string Route,
    string Method,
    string? StatusCode,
    double? DurationMs,
    int Count,
    string? Timestamp,
    string AttributesJson);

public sealed record GbIngestTracesResult(
    string Status,
    int TracesReceived,
    int TracesIngested,
    int EdgesMatched,
    int Unresolved,
    IReadOnlyList<string> Warnings);

public sealed record GbNodeHop(GbNode Node, int Hop);

public sealed record GbTraverseResult(
    GbNode Root,
    IReadOnlyList<GbNodeHop> Visited);

public sealed record GbTraceHop(
    string Name,
    string QualifiedName,
    int Hop,
    string? Risk = null,
    bool? IsTest = null);

public sealed record GbTracePathResult(
    bool Found,
    bool Ambiguous,
    string? FunctionName,
    string? Direction,
    string? Mode,
    IReadOnlyList<GbTraceHop>? Callers,
    IReadOnlyList<GbTraceHop>? Callees,
    string? Note,
    IReadOnlyList<string>? Suggestions,
    string? Error);

public sealed record GbGrepMatch(string File, int Line, string Content);

public sealed record GbSearchCodeHit(
    long NodeId,
    string Node,
    string QualifiedName,
    string Label,
    string File,
    int StartLine,
    int EndLine,
    int InDegree,
    int OutDegree,
    int Score,
    IReadOnlyList<int> MatchLines,
    string? Source = null,
    string? Context = null,
    int? ContextStart = null);

public sealed record GbSearchCodeResult(
    IReadOnlyList<GbSearchCodeHit> Results,
    IReadOnlyList<GbGrepMatch> RawMatches,
    IReadOnlyList<string>? Files,
    IReadOnlyDictionary<string, int> Directories,
    int TotalGrepMatches,
    int TotalResults,
    int RawMatchCount,
    long ElapsedMs,
    string? DedupRatio,
    IReadOnlyList<string> Warnings);

public sealed record GbAdr(
    string Project,
    string Content,
    string CreatedAt,
    string UpdatedAt);

public sealed record GbManageAdrResult(
    string? Content = null,
    string? Status = null,
    string? AdrHint = null,
    IReadOnlyList<string>? Sections = null,
    bool IsWriteError = false);

public sealed record GbGitContext(
    bool IsGit,
    bool IsWorktree,
    bool IsDetached,
    bool RootExists,
    string InputPath,
    string? WorktreeRoot,
    string? GitDir,
    string? GitCommonDir,
    string? CanonicalRoot,
    string? Branch,
    string? BranchSlug,
    string? HeadSha,
    string? BaseSha);

public enum GbGitChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
}

public sealed record GbGitChangedFile(
    string Path,
    GbGitChangeStatus Status,
    string? OldPath = null);

public sealed record GbGitDiffResult(
    bool Success,
    string? ErrorCode,
    string? Hint,
    string BaseRef,
    string? HeadSha,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<GbGitChangedFile>? ChangedFilesWithStatus = null);

public sealed record GbSavedGraphEdge
{
    public string SourceQualifiedName { get; init; } = string.Empty;
    public string TargetQualifiedName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record GbImpactedSymbol(
    string Name,
    string QualifiedName,
    string Label,
    string File,
    int Hop,
    string Direction);

public sealed record GbCallImpactResult(
    int ChangedSymbolCount,
    int ImpactedSymbolCount,
    IReadOnlyList<GbImpactedSymbol> ChangedSymbols,
    IReadOnlyList<GbImpactedSymbol> ImpactedSymbols);

public sealed record GbDetectChangesResult(
    bool Success,
    string? ErrorCode,
    string? Hint,
    string Scope,
    int Depth,
    string Base,
    string? Head,
    string? Branch,
    IReadOnlyList<string> ChangedFiles,
    int ChangedCount,
    IReadOnlyList<GbGitChangedFile>? ChangedFilesWithStatus,
    IReadOnlyList<GbImpactedSymbol> ChangedSymbols,
    int ChangedSymbolCount,
    IReadOnlyList<GbImpactedSymbol> ImpactedSymbols,
    int ImpactedSymbolCount);
