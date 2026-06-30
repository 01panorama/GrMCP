using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmIndexIncrementalTests
{
    [Fact]
    public async Task NoChangeSecondIndexSkipsRebuild()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            var first = await repository.IndexAsync(temp.RootPath);
            var second = await repository.IndexAsync(temp.RootPath);

            Assert.Equal(IndexMode.NoChange, second.Mode);
            Assert.Equal(first.NodeCount, second.NodeCount);
            Assert.Equal(first.EdgeCount, second.EdgeCount);
            Assert.True(second.FileChanges.Unchanged > 0);
            Assert.Equal(0, second.FileChanges.Changed);
            Assert.Equal(0, second.FileChanges.New);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task SingleMethodEditUsesIncremental()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "updated";
                }
                """);

            var result = await repository.IndexAsync(temp.RootPath);

            Assert.Equal(IndexMode.Incremental, result.Mode);
            Assert.True(result.FileChanges.Unchanged > 0);
            Assert.Equal(1, result.FileChanges.Changed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task IncrementalMatchesFullReindex()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "modified";
                }
                """);

            var incremental = await repository.IndexAsync(temp.RootPath);

            using var incrementalStore = CbmStore.OpenPath(incremental.DatabasePath);
            var incrementalCalls = incrementalStore.ListCallGraphEdges(incremental.ProjectName);

            using var freshCache = TempDirectory.Create();
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", freshCache.RootPath);
            using var freshTemp = TempDirectory.Create();
            WriteCallerCalleeFixture(freshTemp.RootPath);
            WriteFile(
                freshTemp.RootPath,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "modified";
                }
                """);

            var full = await repository.IndexAsync(freshTemp.RootPath);
            using var fullStore = CbmStore.OpenPath(full.DatabasePath);
            var fullCalls = fullStore.ListCallGraphEdges(full.ProjectName);

            Assert.Equal(full.NodeCount, incremental.NodeCount);
            Assert.Equal(fullCalls.Count, incrementalCalls.Count);
            foreach (var edge in fullCalls)
            {
                Assert.Contains(
                    incrementalCalls,
                    candidate => QualifiedNameSuffix(candidate.SourceQualifiedName) ==
                        QualifiedNameSuffix(edge.SourceQualifiedName) &&
                        QualifiedNameSuffix(candidate.TargetQualifiedName) ==
                        QualifiedNameSuffix(edge.TargetQualifiedName));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task InboundCallerEdgePreserved()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            var first = await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "still ok";
                }
                """);

            var second = await repository.IndexAsync(temp.RootPath);
            using var store = CbmStore.OpenPath(second.DatabasePath);
            var calls = store.ListCallGraphEdges(second.ProjectName);

            Assert.Equal(IndexMode.Incremental, second.Mode);
            Assert.Contains(
                calls,
                edge => edge.SourceQualifiedName.EndsWith("Sample.Caller.Run()") &&
                    edge.TargetQualifiedName.EndsWith("Sample.Callee.Target()"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task SymbolRenameDropsStaleInbound()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Renamed() => "ok";
                }
                """);

            var result = await repository.IndexAsync(temp.RootPath);
            using var store = CbmStore.OpenPath(result.DatabasePath);
            var calls = store.ListCallGraphEdges(result.ProjectName);

            Assert.DoesNotContain(
                calls,
                edge => edge.TargetQualifiedName.EndsWith("Sample.Callee.Target()"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task DeletedFileRemovesNodesAndHash()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            var first = await repository.IndexAsync(temp.RootPath);

            File.Delete(Path.Combine(temp.RootPath, "Callee.cs"));
            var second = await repository.IndexAsync(temp.RootPath);

            using var store = CbmStore.OpenPath(second.DatabasePath);
            Assert.Equal(IndexMode.Incremental, second.Mode);
            Assert.Equal(1, second.FileChanges.Deleted);
            Assert.Empty(store.FindNodesByFile(second.ProjectName, "Callee.cs"));
            Assert.DoesNotContain(
                store.GetFileHashes(second.ProjectName),
                hash => hash.RelativePath == "Callee.cs");
            Assert.True(second.NodeCount < first.NodeCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task AddedFileIndexedIncrementally()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            var first = await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Extra.cs",
                """
                namespace Sample;

                public sealed class Extra
                {
                    public int Value() => 42;
                }
                """);

            var second = await repository.IndexAsync(temp.RootPath);

            using var store = CbmStore.OpenPath(second.DatabasePath);
            Assert.Equal(IndexMode.Incremental, second.Mode);
            Assert.Equal(1, second.FileChanges.New);
            Assert.NotEmpty(store.FindNodesByFile(second.ProjectName, "Extra.cs"));
            Assert.True(second.NodeCount > first.NodeCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task ProjectFileChangeFallsBackToFull()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var repository = new IndexRepository();
            await repository.IndexAsync(temp.RootPath);

            WriteFile(
                temp.RootPath,
                "Sample.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <RootNamespace>Sample.App</RootNamespace>
                  </PropertyGroup>
                </Project>
                """);

            var result = await repository.IndexAsync(temp.RootPath);

            Assert.Equal(IndexMode.Full, result.Mode);
            Assert.Equal("project_graph_changed", result.FallbackReason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void DeleteNodesByFiles_CascadesIncidentEdges()
    {
        using var store = CbmStore.OpenMemory();
        const string project = "incremental-store";
        store.UpsertProject(project, "/tmp/incremental-store");

        var callerId = store.UpsertNode(FileNode(project, "Caller.cs", "caller.File"));
        var calleeId = store.UpsertNode(FileNode(project, "Callee.cs", "callee.File"));
        store.UpsertEdge(new CbmEdge
        {
            Project = project,
            SourceId = callerId,
            TargetId = calleeId,
            Type = "CALLS",
        });

        Assert.Equal(2, store.CountNodes(project));
        Assert.Equal(1, store.CountEdges(project));

        store.DeleteNodesByFiles(project, ["Callee.cs"]);

        Assert.Equal(1, store.CountNodes(project));
        Assert.Equal(0, store.CountEdges(project));
    }

    [Fact]
    public void SnapshotAndRestore_PreservesInboundCrossFileEdge()
    {
        using var store = CbmStore.OpenMemory();
        const string project = "snapshot-restore";
        store.UpsertProject(project, "/tmp/snapshot-restore");

        var callerId = store.UpsertNode(MethodNode(project, "Run", "Caller.cs", "proj.Caller.Run()"));
        var calleeId = store.UpsertNode(MethodNode(project, "Target", "Callee.cs", "proj.Callee.Target()"));
        store.UpsertEdge(new CbmEdge
        {
            Project = project,
            SourceId = callerId,
            TargetId = calleeId,
            Type = "CALLS",
        });

        var changedPaths = new HashSet<string>(StringComparer.Ordinal) { "Callee.cs" };
        var saved = store.SnapshotInboundCrossFileEdges(project, changedPaths);
        store.DeleteNodesByFiles(project, ["Callee.cs"]);

        var newCalleeId = store.UpsertNode(MethodNode(project, "Target", "Callee.cs", "proj.Callee.Target()"));
        var ids = store.BuildIdsByQualifiedName(project);
        var restored = store.RestoreGraphEdges(project, saved, ids);

        Assert.Equal(1, restored);
        Assert.NotNull(store.FindCallsEdge(project, callerId, newCalleeId));
    }

    private static CbmNode FileNode(string project, string filePath, string qualifiedName)
    {
        return new CbmNode
        {
            Project = project,
            Label = "File",
            Name = Path.GetFileNameWithoutExtension(filePath),
            QualifiedName = qualifiedName,
            FilePath = filePath,
        };
    }

    private static CbmNode MethodNode(string project, string name, string filePath, string qualifiedName)
    {
        return new CbmNode
        {
            Project = project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 5,
        };
    }

    private static string QualifiedNameSuffix(string qualifiedName)
    {
        const string marker = ".Sample.";
        var index = qualifiedName.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0 ? qualifiedName[index..] : qualifiedName;
    }

    private static void WriteCallerCalleeFixture(string root)
    {
        WriteFile(
            root,
            "Sample.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(
            root,
            "Caller.cs",
            """
            namespace Sample;

            public sealed class Caller
            {
                public string Run() => new Callee().Target();
            }
            """);
        WriteFile(
            root,
            "Callee.cs",
            """
            namespace Sample;

            public sealed class Callee
            {
                public string Target() => "ok";
            }
            """);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static TempDirectory Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cbm-incremental-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
