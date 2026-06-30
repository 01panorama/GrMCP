using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class DetectChangesService
{
    private static readonly HashSet<string> ExcludedSymbolLabels = new(StringComparer.Ordinal)
    {
        "File",
        "Folder",
        "Project",
    };

    private readonly GitDiffService gitDiffService;
    private readonly CallImpactService callImpactService;

    public DetectChangesService()
        : this(new GitDiffService(), new CallImpactService())
    {
    }

    public DetectChangesService(GitDiffService gitDiffService, CallImpactService callImpactService)
    {
        this.gitDiffService = gitDiffService;
        this.callImpactService = callImpactService;
    }

    public CbmDetectChangesResult Detect(
        string projectName,
        string? baseBranch = null,
        string? since = null,
        string? scope = null,
        int depth = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        var normalizedScope = ResolveScope(scope);
        var reportedScope = normalizedScope ?? scope ?? "symbols";

        if (!File.Exists(databasePath))
        {
            return Failure(
                scope: reportedScope,
                depth,
                baseRef: ResolveBaseRef(baseBranch, since),
                errorCode: "project_not_found",
                hint: "Call index_repository first.");
        }

        using var store = CbmStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        if (project is null)
        {
            return Failure(
                scope: reportedScope,
                depth,
                baseRef: ResolveBaseRef(baseBranch, since),
                errorCode: "project_not_found",
                hint: "Call index_repository first.");
        }

        var rootPath = project.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return Failure(
                scope: reportedScope,
                depth,
                baseRef: ResolveBaseRef(baseBranch, since),
                errorCode: "project_not_found",
                hint: "Project has no stored root path.");
        }

        if (normalizedScope is null)
        {
            return Failure(
                scope: reportedScope,
                depth,
                baseRef: ResolveBaseRef(baseBranch, since),
                errorCode: "invalid_scope",
                hint: "scope must be one of: files, symbols, impact.");
        }

        var baseRef = ResolveBaseRef(baseBranch, since);
        var diff = gitDiffService.GetChangedFilesWithStatus(rootPath, baseRef);
        if (!diff.Success)
        {
            return new CbmDetectChangesResult(
                Success: false,
                ErrorCode: diff.ErrorCode,
                Hint: diff.Hint,
                Scope: normalizedScope,
                Depth: depth,
                Base: baseRef,
                Head: diff.HeadSha,
                Branch: null,
                ChangedFiles: [],
                ChangedCount: 0,
                ChangedFilesWithStatus: null,
                ChangedSymbols: [],
                ChangedSymbolCount: 0,
                ImpactedSymbols: [],
                ImpactedSymbolCount: 0);
        }

        var gitContext = GitContextResolver.Resolve(rootPath);
        var changedFiles = diff.ChangedFiles;
        var changedSymbols = Array.Empty<CbmImpactedSymbol>();
        var impactedSymbols = Array.Empty<CbmImpactedSymbol>();

        if (normalizedScope == "impact")
        {
            var impact = callImpactService.Propagate(projectName, changedFiles, depth);
            changedSymbols = impact.ChangedSymbols.ToArray();
            impactedSymbols = impact.ImpactedSymbols.ToArray();
        }
        else if (normalizedScope == "symbols")
        {
            changedSymbols = ListChangedSymbols(store, projectName, changedFiles);
        }

        return new CbmDetectChangesResult(
            Success: true,
            ErrorCode: null,
            Hint: null,
            Scope: normalizedScope,
            Depth: depth,
            Base: baseRef,
            Head: gitContext.HeadSha ?? diff.HeadSha,
            Branch: gitContext.Branch,
            ChangedFiles: changedFiles,
            ChangedCount: changedFiles.Count,
            ChangedFilesWithStatus: diff.ChangedFilesWithStatus,
            ChangedSymbols: changedSymbols,
            ChangedSymbolCount: changedSymbols.Length,
            ImpactedSymbols: impactedSymbols,
            ImpactedSymbolCount: impactedSymbols.Length);
    }

    private static string ResolveBaseRef(string? baseBranch, string? since)
    {
        if (!string.IsNullOrWhiteSpace(since))
        {
            return since;
        }

        if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            return baseBranch;
        }

        return "main";
    }

    private static string? ResolveScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "symbols";
        }

        return scope switch
        {
            "files" => "files",
            "symbols" => "symbols",
            "impact" => "impact",
            _ => null,
        };
    }

    private static CbmImpactedSymbol[] ListChangedSymbols(
        CbmStore store,
        string projectName,
        IReadOnlyList<string> changedFiles)
    {
        var symbols = new List<CbmImpactedSymbol>();

        foreach (var relativePath in changedFiles.Distinct(StringComparer.Ordinal))
        {
            foreach (var node in store.FindNodesByFile(projectName, relativePath))
            {
                if (ExcludedSymbolLabels.Contains(node.Label))
                {
                    continue;
                }

                symbols.Add(new CbmImpactedSymbol(
                    node.Name,
                    node.QualifiedName,
                    node.Label,
                    node.FilePath,
                    Hop: 0,
                    Direction: "changed"));
            }
        }

        return symbols
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();
    }

    private static CbmDetectChangesResult Failure(
        string scope,
        int depth,
        string baseRef,
        string errorCode,
        string hint)
    {
        return new CbmDetectChangesResult(
            Success: false,
            ErrorCode: errorCode,
            Hint: hint,
            Scope: scope,
            Depth: depth,
            Base: baseRef,
            Head: null,
            Branch: null,
            ChangedFiles: [],
            ChangedCount: 0,
            ChangedFilesWithStatus: null,
            ChangedSymbols: [],
            ChangedSymbolCount: 0,
            ImpactedSymbols: [],
            ImpactedSymbolCount: 0);
    }
}
