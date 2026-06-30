using Cbm.Graph;
using Cbm.Roslyn;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed record IncrementalPatchResult(
    int NodesPatched,
    int EdgesRestored);

public sealed class IncrementalIndexService
{
    private readonly CSharpProjectLoader projectLoader = new();
    private readonly CSharpDefinitionExtractor definitionExtractor = new();
    private readonly CSharpRelationshipExtractor relationshipExtractor = new();
    private readonly CSharpDiscovery discovery = new();

    public async Task<IncrementalPatchResult> PatchAsync(
        string projectName,
        string repositoryRoot,
        string repoPath,
        string databasePath,
        FileChangeClassification classification,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(classification);

        var changedPaths = BuildChangedPaths(classification);
        using var store = CbmStore.OpenPath(databasePath);

        var savedEdges = store.SnapshotInboundCrossFileEdges(projectName, changedPaths);
        if (changedPaths.Count > 0)
        {
            store.DeleteNodesByFiles(projectName, changedPaths);
        }

        foreach (var deleted in classification.Deleted)
        {
            store.DeleteFileHash(projectName, deleted);
        }

        var changedRelativePaths = classification.ChangedOrNew
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        var nodesPatched = 0;
        var edgesRestored = 0;

        if (changedRelativePaths.Count > 0)
        {
            var patch = await ExtractPatchAsync(
                projectName,
                repositoryRoot,
                repoPath,
                changedRelativePaths,
                cancellationToken).ConfigureAwait(false);

            var knownQualifiedNames = store.ListQualifiedNames(projectName)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var node in patch.Definitions.Nodes)
            {
                knownQualifiedNames.Add(node.QualifiedName);
            }

            IReadOnlyList<CbmGraphEdge> relationships;
            if (patch.IsLoose)
            {
                relationships = relationshipExtractor.ExtractFromLooseDocuments(
                    projectName,
                    repositoryRoot,
                    patch.LooseDocuments!,
                    knownQualifiedNames);
            }
            else
            {
                relationships = relationshipExtractor.ExtractFromLoadedDocuments(
                    projectName,
                    repositoryRoot,
                    patch.Documents,
                    knownQualifiedNames);
            }

            var mergedEdges = new List<CbmGraphEdge>(patch.Definitions.Edges.Count + relationships.Count);
            mergedEdges.AddRange(patch.Definitions.Edges);
            mergedEdges.AddRange(relationships);
            var nodes = CSharpTransitiveLoopDepth.Apply(patch.Definitions.Nodes, mergedEdges);

            store.UpsertNodeBatch(nodes, rebuildFts: false);
            nodesPatched = nodes.Count;

            var idsByQualifiedName = store.BuildIdsByQualifiedName(projectName);
            store.UpsertGraphEdges(projectName, idsByQualifiedName, mergedEdges);
            edgesRestored = store.RestoreGraphEdges(projectName, savedEdges, idsByQualifiedName);
        }
        else
        {
            var idsByQualifiedName = store.BuildIdsByQualifiedName(projectName);
            edgesRestored = store.RestoreGraphEdges(projectName, savedEdges, idsByQualifiedName);
        }

        RefreshTransitiveLoopDepth(store, projectName);
        PersistChangedHashes(store, projectName, repositoryRoot, classification);
        store.UpsertProject(projectName, repositoryRoot);
        store.RefreshFtsIndex();

        return new IncrementalPatchResult(nodesPatched, edgesRestored);
    }

    private static HashSet<string> BuildChangedPaths(FileChangeClassification classification)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in classification.ChangedOrNew)
        {
            paths.Add(file.RelativePath);
        }

        foreach (var deleted in classification.Deleted)
        {
            paths.Add(deleted);
        }

        return paths;
    }

    private async Task<PatchExtraction> ExtractPatchAsync(
        string projectName,
        string repositoryRoot,
        string repoPath,
        IReadOnlySet<string> changedRelativePaths,
        CancellationToken cancellationToken)
    {
        var workspaceEntryPoint = ResolveWorkspaceEntryPoint(repoPath, repositoryRoot);
        if (workspaceEntryPoint is not null)
        {
            var loaded = await projectLoader.LoadAsync(workspaceEntryPoint, cancellationToken)
                .ConfigureAwait(false);
            if (loaded.Diagnostics.Count > 0 && loaded.Projects.Count == 0)
            {
                throw new InvalidOperationException("workspace_load_failed");
            }

            var documents = loaded.Projects
                .SelectMany(project => project.Documents)
                .Where(document => IsChangedDocument(repositoryRoot, document, changedRelativePaths))
                .ToArray();
            var definitions = definitionExtractor.ExtractFromLoadedDocuments(
                projectName,
                repositoryRoot,
                documents);
            return new PatchExtraction(definitions, documents, [], IsLoose: false);
        }

        var discoveredFiles = discovery.Discover(repositoryRoot);
        var changedFiles = discoveredFiles
            .Where(file => changedRelativePaths.Contains(file.RelativePath))
            .ToArray();
        var loose = LooseCSharpCompilation.CreateFromFiles(changedFiles.Select(file => file.Path));
        var looseDocuments = loose.Documents
            .Where(document => IsChangedLooseDocument(repositoryRoot, document, changedRelativePaths))
            .ToArray();
        var looseDefinitions = definitionExtractor.ExtractFromLooseDocuments(
            projectName,
            repositoryRoot,
            looseDocuments);
        return new PatchExtraction(looseDefinitions, [], looseDocuments, IsLoose: true);
    }

    private static bool IsChangedDocument(
        string repositoryRoot,
        CSharpLoadedDocument document,
        IReadOnlySet<string> changedRelativePaths)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            return false;
        }

        var relativePath = CSharpQualifiedName.ToRelativePath(repositoryRoot, document.FilePath);
        return changedRelativePaths.Contains(relativePath);
    }

    private static bool IsChangedLooseDocument(
        string repositoryRoot,
        CSharpLooseDocument document,
        IReadOnlySet<string> changedRelativePaths)
    {
        var relativePath = CSharpQualifiedName.ToRelativePath(repositoryRoot, document.FilePath);
        return changedRelativePaths.Contains(relativePath);
    }

    private static void RefreshTransitiveLoopDepth(CbmStore store, string projectName)
    {
        var methodNodes = store.ListNodesByLabels(projectName, ["Method", "Constructor"]);
        if (methodNodes.Count == 0)
        {
            return;
        }

        var callEdges = store.ListCallGraphEdges(projectName);
        var updated = CSharpTransitiveLoopDepth.Apply(methodNodes, callEdges);
        store.UpsertNodeBatch(updated, rebuildFts: false);
    }

    private static void PersistChangedHashes(
        CbmStore store,
        string projectName,
        string repositoryRoot,
        FileChangeClassification classification)
    {
        var rows = new List<CbmFileHash>(classification.ChangedOrNew.Count);
        foreach (var fingerprint in classification.ChangedOrNew)
        {
            if (!File.Exists(fingerprint.Path))
            {
                continue;
            }

            var refreshed = CSharpFileFingerprint.Collect(repositoryRoot, fingerprint.Path);
            rows.Add(new CbmFileHash
            {
                Project = projectName,
                RelativePath = refreshed.RelativePath,
                Sha256 = refreshed.Sha256,
                MtimeNs = refreshed.MtimeNs,
                Size = refreshed.Size,
            });
        }

        if (rows.Count > 0)
        {
            store.UpsertFileHashBatch(rows);
        }
    }

    private static string? ResolveWorkspaceEntryPoint(string repoPath, string repositoryRoot)
    {
        var fullPath = Path.GetFullPath(repoPath);
        if (File.Exists(fullPath) && IsWorkspaceFile(fullPath))
        {
            return fullPath;
        }

        var solutions = Directory.GetFiles(repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly);
        if (solutions.Length > 0)
        {
            return solutions.OrderBy(path => path, StringComparer.Ordinal).First();
        }

        var projects = Directory.GetFiles(repositoryRoot, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length > 0)
        {
            return projects.OrderBy(path => path, StringComparer.Ordinal).First();
        }

        return null;
    }

    private static bool IsWorkspaceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PatchExtraction(
        CbmDefinitionExtractionResult Definitions,
        IReadOnlyList<CSharpLoadedDocument> Documents,
        IReadOnlyList<CSharpLooseDocument> LooseDocuments,
        bool IsLoose);
}
