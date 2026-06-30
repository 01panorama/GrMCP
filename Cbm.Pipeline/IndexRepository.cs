using Cbm.Graph;
using Cbm.Roslyn;
using Cbm.Store;

namespace Cbm.Pipeline;

public enum IndexMode
{
    Full,
    Incremental,
    NoChange,
}

public sealed record IndexRepositoryResult(
    string ProjectName,
    string RepositoryRoot,
    string DatabasePath,
    int NodeCount,
    int EdgeCount,
    FileChangeCounts FileChanges,
    IndexMode Mode = IndexMode.Full,
    string? FallbackReason = null);

public sealed class IndexRepository
{
    private readonly CSharpProjectLoader projectLoader = new();
    private readonly CSharpDefinitionExtractor definitionExtractor = new();
    private readonly CSharpRelationshipExtractor relationshipExtractor = new();
    private readonly CSharpDiscovery discovery = new();
    private readonly IncrementalIndexService incrementalIndexService = new();

    public async Task<IndexRepositoryResult> IndexAsync(
        string repoPath,
        string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var repositoryRoot = ResolveRepositoryRoot(repoPath);
        var projectName = CbmProjectNaming.DeriveFromPath(repositoryRoot);
        var targetDatabasePath = string.IsNullOrWhiteSpace(databasePath)
            ? CbmCachePaths.GetProjectDatabasePath(projectName)
            : Path.GetFullPath(databasePath);

        var databaseExists = File.Exists(targetDatabasePath);
        var (savedAdr, storedHashes) = SnapshotPriorState(targetDatabasePath, projectName);
        var fingerprints = await DiscoverFingerprintsAsync(
            repositoryRoot,
            repoPath,
            cancellationToken).ConfigureAwait(false);
        var classification = FileChangeClassifier.Classify(repositoryRoot, fingerprints, storedHashes);

        if (databaseExists && IncrementalIndexPolicy.IsNoChange(classification))
        {
            using var existingStore = CbmStore.OpenPath(targetDatabasePath);
            return new IndexRepositoryResult(
                projectName,
                repositoryRoot,
                targetDatabasePath,
                existingStore.CountNodes(projectName),
                existingStore.CountEdges(projectName),
                classification.Counts,
                IndexMode.NoChange);
        }

        var fallbackReason = IncrementalIndexPolicy.GetFallbackReason(
            databaseExists,
            storedHashes.Count,
            classification,
            fingerprints.Count);

        if (fallbackReason is null)
        {
            try
            {
                await incrementalIndexService.PatchAsync(
                    projectName,
                    repositoryRoot,
                    repoPath,
                    targetDatabasePath,
                    classification,
                    cancellationToken).ConfigureAwait(false);

                using var verificationStore = CbmStore.OpenPath(targetDatabasePath);
                return new IndexRepositoryResult(
                    projectName,
                    repositoryRoot,
                    targetDatabasePath,
                    verificationStore.CountNodes(projectName),
                    verificationStore.CountEdges(projectName),
                    classification.Counts,
                    IndexMode.Incremental);
            }
            catch (InvalidOperationException exception)
                when (exception.Message is "workspace_load_failed" or "compilation_failed")
            {
                fallbackReason = exception.Message;
            }
        }

        var extraction = await ExtractGraphAsync(
            projectName,
            repositoryRoot,
            repoPath,
            cancellationToken).ConfigureAwait(false);

        PersistExtraction(
            projectName,
            repositoryRoot,
            targetDatabasePath,
            extraction,
            fingerprints,
            classification.ModeSkipped,
            savedAdr);

        using var fullVerificationStore = CbmStore.OpenPath(targetDatabasePath);
        return new IndexRepositoryResult(
            projectName,
            repositoryRoot,
            targetDatabasePath,
            fullVerificationStore.CountNodes(projectName),
            fullVerificationStore.CountEdges(projectName),
            classification.Counts,
            IndexMode.Full,
            fallbackReason);
    }

    private static (string? SavedAdr, IReadOnlyList<CbmFileHash> StoredHashes) SnapshotPriorState(
        string databasePath,
        string projectName)
    {
        if (!File.Exists(databasePath))
        {
            return (null, []);
        }

        using var store = CbmStore.OpenPath(databasePath);
        var adr = store.AdrGet(projectName);
        return (adr?.Content, store.GetFileHashes(projectName));
    }

    private async Task<IReadOnlyList<CbmIndexedFileFingerprint>> DiscoverFingerprintsAsync(
        string repositoryRoot,
        string repoPath,
        CancellationToken cancellationToken)
    {
        var workspaceEntryPoint = ResolveWorkspaceEntryPoint(repoPath, repositoryRoot);
        if (workspaceEntryPoint is not null)
        {
            var loaded = await projectLoader.LoadAsync(workspaceEntryPoint, cancellationToken)
                .ConfigureAwait(false);
            var documents = loaded.Projects.SelectMany(project => project.Documents).ToArray();
            var fingerprints = CollectWorkspaceFingerprints(repositoryRoot, documents).ToList();
            var trackedPaths = fingerprints
                .Select(fingerprint => fingerprint.RelativePath)
                .ToHashSet(StringComparer.Ordinal);

            var workspacePaths = new List<string> { loaded.Path };
            workspacePaths.AddRange(
                loaded.Projects
                    .Select(project => project.FilePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))!);

            foreach (var absolutePath in workspacePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = CSharpQualifiedName.ToRelativePath(repositoryRoot, absolutePath);
                if (trackedPaths.Add(relativePath))
                {
                    fingerprints.Add(CSharpFileFingerprint.Collect(repositoryRoot, absolutePath));
                }
            }

            return fingerprints;
        }

        var discoveredFiles = discovery.Discover(repositoryRoot);
        return discoveredFiles
            .Select(file => CSharpFileFingerprint.FromDiscovered(file))
            .ToArray();
    }

    private async Task<CbmDefinitionExtractionResult> ExtractGraphAsync(
        string projectName,
        string repositoryRoot,
        string repoPath,
        CancellationToken cancellationToken)
    {
        var workspaceEntryPoint = ResolveWorkspaceEntryPoint(repoPath, repositoryRoot);
        if (workspaceEntryPoint is not null)
        {
            var loaded = await projectLoader.LoadAsync(workspaceEntryPoint, cancellationToken)
                .ConfigureAwait(false);
            var documents = loaded.Projects.SelectMany(project => project.Documents).ToArray();
            var definitions = definitionExtractor.ExtractFromLoadedDocuments(
                projectName,
                repositoryRoot,
                documents);
            var knownQualifiedNames = definitions.Nodes
                .Select(node => node.QualifiedName)
                .ToHashSet(StringComparer.Ordinal);
            var relationships = relationshipExtractor.ExtractFromLoadedDocuments(
                projectName,
                repositoryRoot,
                documents,
                knownQualifiedNames);
            return MergeExtraction(definitions, relationships);
        }

        var discoveredFiles = discovery.Discover(repositoryRoot);
        if (discoveredFiles.Count == 0)
        {
            throw new InvalidOperationException($"No C# sources discovered under '{repositoryRoot}'.");
        }

        var loose = LooseCSharpCompilation.CreateFromFiles(discoveredFiles.Select(file => file.Path));
        var looseDefinitions = definitionExtractor.ExtractFromLooseDocuments(
            projectName,
            repositoryRoot,
            loose.Documents);
        var looseKnownQualifiedNames = looseDefinitions.Nodes
            .Select(node => node.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        var looseRelationships = relationshipExtractor.ExtractFromLooseDocuments(
            projectName,
            repositoryRoot,
            loose.Documents,
            looseKnownQualifiedNames);
        return MergeExtraction(looseDefinitions, looseRelationships);
    }

    private static IReadOnlyList<CbmIndexedFileFingerprint> CollectWorkspaceFingerprints(
        string repositoryRoot,
        IReadOnlyList<CSharpLoadedDocument> documents)
    {
        var paths = documents
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return CSharpFileFingerprint.CollectMany(repositoryRoot, paths);
    }

    private static CbmDefinitionExtractionResult MergeExtraction(
        CbmDefinitionExtractionResult definitions,
        IReadOnlyList<CbmGraphEdge> relationships)
    {
        var mergedEdges = new List<CbmGraphEdge>(definitions.Edges.Count + relationships.Count);
        mergedEdges.AddRange(definitions.Edges);
        mergedEdges.AddRange(relationships);
        var nodes = CSharpTransitiveLoopDepth.Apply(definitions.Nodes, mergedEdges);
        return new CbmDefinitionExtractionResult(nodes, mergedEdges);
    }

    private static void PersistExtraction(
        string projectName,
        string repositoryRoot,
        string databasePath,
        CbmDefinitionExtractionResult extraction,
        IReadOnlyList<CbmIndexedFileFingerprint> fingerprints,
        IReadOnlyList<CbmFileHash> modeSkippedHashes,
        string? savedAdr)
    {
        DeleteExistingDatabase(databasePath);

        using var store = CbmStore.OpenMemory();
        store.BeginBulk();
        store.UpsertProject(projectName, repositoryRoot);

        var nodeIds = store.UpsertNodeBatch(extraction.Nodes);
        var idsByQualifiedName = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var index = 0; index < extraction.Nodes.Count; index++)
        {
            idsByQualifiedName[extraction.Nodes[index].QualifiedName] = nodeIds[index];
        }

        store.UpsertGraphEdges(projectName, idsByQualifiedName, extraction.Edges);
        store.EndBulk();
        store.DumpToFile(databasePath);

        using var persistedStore = CbmStore.OpenPath(databasePath);
        if (!string.IsNullOrEmpty(savedAdr))
        {
            persistedStore.AdrStore(projectName, savedAdr);
        }

        persistedStore.DeleteFileHashes(projectName);
        var hashRows = BuildPersistedFileHashes(projectName, repositoryRoot, fingerprints, modeSkippedHashes);
        if (hashRows.Count > 0)
        {
            persistedStore.UpsertFileHashBatch(hashRows);
        }
    }

    private static List<CbmFileHash> BuildPersistedFileHashes(
        string projectName,
        string repositoryRoot,
        IReadOnlyList<CbmIndexedFileFingerprint> fingerprints,
        IReadOnlyList<CbmFileHash> modeSkippedHashes)
    {
        var rows = new List<CbmFileHash>(fingerprints.Count + modeSkippedHashes.Count);

        foreach (var fingerprint in fingerprints)
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

        foreach (var skipped in modeSkippedHashes)
        {
            rows.Add(new CbmFileHash
            {
                Project = projectName,
                RelativePath = skipped.RelativePath,
                Sha256 = skipped.Sha256,
                MtimeNs = skipped.MtimeNs,
                Size = skipped.Size,
            });
        }

        return rows;
    }

    private static void DeleteExistingDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        File.Delete(databasePath);
        var walPath = databasePath + "-wal";
        var shmPath = databasePath + "-shm";
        if (File.Exists(walPath))
        {
            File.Delete(walPath);
        }

        if (File.Exists(shmPath))
        {
            File.Delete(shmPath);
        }
    }

    private static string ResolveRepositoryRoot(string repoPath)
    {
        var fullPath = Path.GetFullPath(repoPath);
        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath)
                ?? throw new DirectoryNotFoundException($"Could not resolve repository root for '{repoPath}'.");
        }

        throw new DirectoryNotFoundException($"Repository path not found: '{repoPath}'.");
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
}
