using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmTracePathServiceTests
{
    private const string Project = "trace-service";

    [Fact]
    public async Task Trace_FindsInboundAndOutboundCallersForIndexedFixture()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteProject(temp.Path);
            WriteFile(
                temp.Path,
                "Caller.cs",
                """
                namespace Sample;

                public sealed class Caller
                {
                    public string Run() => new Callee().Target();
                }
                """);
            WriteFile(
                temp.Path,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "ok";
                }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var service = new TracePathService();

            var inbound = service.Trace(indexResult.ProjectName, "Target", direction: "inbound");
            var outbound = service.Trace(indexResult.ProjectName, "Run", direction: "outbound");

            Assert.True(inbound.Found);
            Assert.Contains(inbound.Callers!, hop => hop.Name == "Run");
            Assert.True(outbound.Found);
            Assert.Contains(outbound.Callees!, hop => hop.Name == "Target");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Trace_ReturnsAmbiguousSuggestionsForEqualRankMatches()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(store =>
            {
                store.UpsertNode(MethodNode("amb", "trace-service.a.amb", "a.cs", 10, 20));
                store.UpsertNode(MethodNode("amb", "trace-service.b.amb", "b.cs", 10, 20));
            });

            var result = new TracePathService().Trace(Project, "amb");

            Assert.False(result.Found);
            Assert.True(result.Ambiguous);
            Assert.Equal(2, result.Suggestions!.Count);
            Assert.Null(result.Callers);
            Assert.Null(result.Callees);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Trace_PrefersCallableDefinitionOverModuleMatch()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(store =>
            {
                var wrongId = store.UpsertNode(new CbmNode
                {
                    Project = Project,
                    Label = "Namespace",
                    Name = "dup",
                    QualifiedName = "trace-service.dup",
                    FilePath = "dup.x",
                    StartLine = 1,
                    EndLine = 1,
                });
                var defId = store.UpsertNode(MethodNode("dup", "trace-service.src.dup", "src/dup.cs", 10, 50));
                var calleeId = store.UpsertNode(MethodNode("callee", "trace-service.src.callee", "src/dup.cs", 60, 70));
                store.UpsertEdge(new CbmEdge
                {
                    Project = Project,
                    SourceId = defId,
                    TargetId = calleeId,
                    Type = "CALLS",
                });
                Assert.NotEqual(wrongId, defId);
            });

            var result = new TracePathService().Trace(Project, "dup", direction: "outbound");

            Assert.True(result.Found);
            Assert.False(result.Ambiguous);
            Assert.Contains(result.Callees!, hop => hop.Name == "callee");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Trace_CrossService_ReturnsEmptyWithNote()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(store =>
            {
                store.UpsertNode(MethodNode("Main", "trace-service.Main", "main.cs", 1, 10));
            });

            var result = new TracePathService().Trace(Project, "Main", mode: "cross_service");

            Assert.True(result.Found);
            Assert.Empty(result.Callers!);
            Assert.Empty(result.Callees!);
            Assert.Contains("HTTP Route", result.Note!, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Trace_NotFound_ReturnsSearchHint()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(_ => { });

            var result = new TracePathService().Trace(Project, "Missing");

            Assert.False(result.Found);
            Assert.Contains("search_graph", result.Error!, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Trace_AppliesRiskLabelsAndFiltersTests()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(store =>
            {
                var rootId = store.UpsertNode(MethodNode("Root", "trace-service.Root", "src/root.cs", 1, 5));
                var prodId = store.UpsertNode(MethodNode("Prod", "trace-service.Prod", "src/prod.cs", 1, 5));
                var testId = store.UpsertNode(MethodNode("TestFn", "trace-service.TestFn", "tests/test_fn.cs", 1, 5));
                store.UpsertEdge(Edge(rootId, prodId, "CALLS"));
                store.UpsertEdge(Edge(rootId, testId, "CALLS"));
            });

            var filtered = new TracePathService().Trace(Project, "Root", direction: "outbound", depth: 1);
            var withTests = new TracePathService().Trace(
                Project,
                "Root",
                direction: "outbound",
                depth: 1,
                includeTests: true);
            var withRisk = new TracePathService().Trace(
                Project,
                "Root",
                direction: "outbound",
                depth: 1,
                riskLabels: true);

            Assert.Single(filtered.Callees!);
            Assert.Equal("Prod", filtered.Callees![0].Name);
            Assert.Equal(2, withTests.Callees!.Count);
            Assert.All(withRisk.Callees!, hop => Assert.Equal("CRITICAL", hop.Risk));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static void SeedProject(Action<CbmStore> seed)
    {
        var path = CbmCachePaths.GetProjectDatabasePath(Project);
        using var store = CbmStore.OpenPath(path);
        store.UpsertProject(Project, "/tmp/trace-service");
        seed(store);
    }

    private static CbmNode MethodNode(
        string name,
        string qualifiedName,
        string filePath,
        int startLine,
        int endLine)
    {
        return new CbmNode
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
        };
    }

    private static CbmEdge Edge(long sourceId, long targetId, string type)
    {
        return new CbmEdge
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
        };
    }

    private static void WriteProject(string root)
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
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cbm-trace-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
