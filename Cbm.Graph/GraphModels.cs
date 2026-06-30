namespace Cbm.Graph;

public sealed record CbmNode
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

public sealed record CbmEdge
{
    public long Id { get; init; }
    public string Project { get; init; } = string.Empty;
    public long SourceId { get; init; }
    public long TargetId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record CbmProject
{
    public string Name { get; init; } = string.Empty;
    public string IndexedAt { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
}

public sealed record CbmFileHash
{
    public string Project { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long MtimeNs { get; init; }
    public long Size { get; init; }
}

public sealed record CbmNodeDegree(int InDegree, int OutDegree);

public sealed record CbmNodeNeighbors(
    IReadOnlyList<string> Callers,
    IReadOnlyList<string> Callees);

public sealed record CbmGraphEdge
{
    public string Project { get; init; } = string.Empty;
    public string SourceQualifiedName { get; init; } = string.Empty;
    public string TargetQualifiedName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record CbmDefinitionExtractionResult(
    IReadOnlyList<CbmNode> Nodes,
    IReadOnlyList<CbmGraphEdge> Edges);

public sealed record CbmCachedProject(
    string Name,
    string IndexedAt,
    string RootPath,
    long SizeBytes,
    int NodeCount,
    int EdgeCount);

public sealed record CbmIndexStatus(
    string Project,
    string RootPath,
    int Nodes,
    int Edges,
    string Status);

public sealed record CbmLabelSchema(string Label, int Count);

public sealed record CbmEdgeTypeSchema(string Type, int Count);

public sealed record CbmGraphSchema(
    IReadOnlyList<CbmLabelSchema> NodeLabels,
    IReadOnlyList<CbmEdgeTypeSchema> EdgeTypes);

public sealed record CbmSearchGraphResult(
    IReadOnlyList<CbmNode> Results,
    int Total,
    int Offset,
    int Limit,
    bool HasMore);

public sealed record CbmCodeSnippetResult(
    bool Found,
    string? QualifiedName,
    string? FilePath,
    int StartLine,
    int EndLine,
    string? Code,
    string? MatchType,
    IReadOnlyList<string>? Suggestions,
    string? Error);

public sealed record CbmCypherQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Hint = null);

public sealed record CbmLanguageCount(string Language, int FileCount);

public sealed record CbmPackageSummary(
    string Name,
    int NodeCount,
    int FanIn,
    int FanOut);

public sealed record CbmEntryPoint(
    string Name,
    string QualifiedName,
    string File);

public sealed record CbmHotspot(
    string Name,
    string QualifiedName,
    int FanIn);

public sealed record CbmCrossPackageBoundary(
    string From,
    string To,
    int CallCount);

public sealed record CbmPackageLayer(
    string Name,
    string Layer,
    string Reason);

public sealed record CbmClusterInfo(
    int Id,
    string Label,
    int Members,
    double Cohesion,
    IReadOnlyList<string> TopNodes,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> EdgeTypes);

public sealed record CbmFileTreeEntry(
    string Path,
    string Type,
    int Children);

public sealed record CbmArchitectureResult(
    string Project,
    string? Path,
    int TotalNodes,
    int TotalEdges,
    int? RootTotalNodes,
    int? RootTotalEdges,
    CbmGraphSchema? Structure,
    CbmGraphSchema? Dependencies,
    IReadOnlyList<CbmLanguageCount>? Languages,
    IReadOnlyList<CbmPackageSummary>? Packages,
    IReadOnlyList<CbmEntryPoint>? EntryPoints,
    IReadOnlyList<CbmHotspot>? Hotspots,
    IReadOnlyList<CbmCrossPackageBoundary>? Boundaries,
    IReadOnlyList<CbmPackageLayer>? Layers,
    IReadOnlyList<CbmClusterInfo>? Clusters,
    IReadOnlyList<CbmFileTreeEntry>? FileTree,
    CbmRuntimeSummary? Runtime = null);

public sealed record CbmRuntimeSummary(
    int TotalObservations,
    int MatchedEdges,
    IReadOnlyList<CbmRuntimeObservation> Observations);

public sealed record CbmRuntimeObservation(
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

public sealed record CbmNormalizedTraceEntry(
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

public sealed record CbmIngestTracesResult(
    string Status,
    int TracesReceived,
    int TracesIngested,
    int EdgesMatched,
    int Unresolved,
    IReadOnlyList<string> Warnings);

public sealed record CbmNodeHop(CbmNode Node, int Hop);

public sealed record CbmTraverseResult(
    CbmNode Root,
    IReadOnlyList<CbmNodeHop> Visited);

public sealed record CbmTraceHop(
    string Name,
    string QualifiedName,
    int Hop,
    string? Risk = null,
    bool? IsTest = null);

public sealed record CbmTracePathResult(
    bool Found,
    bool Ambiguous,
    string? FunctionName,
    string? Direction,
    string? Mode,
    IReadOnlyList<CbmTraceHop>? Callers,
    IReadOnlyList<CbmTraceHop>? Callees,
    string? Note,
    IReadOnlyList<string>? Suggestions,
    string? Error);

public sealed record CbmGrepMatch(string File, int Line, string Content);

public sealed record CbmSearchCodeHit(
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

public sealed record CbmSearchCodeResult(
    IReadOnlyList<CbmSearchCodeHit> Results,
    IReadOnlyList<CbmGrepMatch> RawMatches,
    IReadOnlyList<string>? Files,
    IReadOnlyDictionary<string, int> Directories,
    int TotalGrepMatches,
    int TotalResults,
    int RawMatchCount,
    long ElapsedMs,
    string? DedupRatio,
    IReadOnlyList<string> Warnings);

public sealed record CbmAdr(
    string Project,
    string Content,
    string CreatedAt,
    string UpdatedAt);

public sealed record CbmManageAdrResult(
    string? Content = null,
    string? Status = null,
    string? AdrHint = null,
    IReadOnlyList<string>? Sections = null,
    bool IsWriteError = false);

public sealed record CbmGitContext(
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

public enum CbmGitChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
}

public sealed record CbmGitChangedFile(
    string Path,
    CbmGitChangeStatus Status,
    string? OldPath = null);

public sealed record CbmGitDiffResult(
    bool Success,
    string? ErrorCode,
    string? Hint,
    string BaseRef,
    string? HeadSha,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<CbmGitChangedFile>? ChangedFilesWithStatus = null);

public sealed record CbmSavedGraphEdge
{
    public string SourceQualifiedName { get; init; } = string.Empty;
    public string TargetQualifiedName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string PropertiesJson { get; init; } = "{}";
}

public sealed record CbmImpactedSymbol(
    string Name,
    string QualifiedName,
    string Label,
    string File,
    int Hop,
    string Direction);

public sealed record CbmCallImpactResult(
    int ChangedSymbolCount,
    int ImpactedSymbolCount,
    IReadOnlyList<CbmImpactedSymbol> ChangedSymbols,
    IReadOnlyList<CbmImpactedSymbol> ImpactedSymbols);

public sealed record CbmDetectChangesResult(
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
    IReadOnlyList<CbmGitChangedFile>? ChangedFilesWithStatus,
    IReadOnlyList<CbmImpactedSymbol> ChangedSymbols,
    int ChangedSymbolCount,
    IReadOnlyList<CbmImpactedSymbol> ImpactedSymbols,
    int ImpactedSymbolCount);
