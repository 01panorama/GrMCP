using Gb.Graph;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbTracePathServiceTests : IClassFixture<GbTestCacheClassFixture>
{
    private const string Project = "trace-service";

    private readonly GbTestCacheClassFixture cache;

    public GbTracePathServiceTests(GbTestCacheClassFixture cache)
    {
        this.cache = cache;
    }

    [Fact]
    public void Trace_ReturnsAmbiguousSuggestionsForEqualRankMatches()
    {
        cache.ClearCacheDirectory();
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

    [Fact]
    public void Trace_PrefersCallableDefinitionOverModuleMatch()
    {
        cache.ClearCacheDirectory();
        SeedProject(store =>
        {
            var wrongId = store.UpsertNode(new GbNode
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
            store.UpsertEdge(new GbEdge
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

    [Fact]
    public void Trace_CrossService_ReturnsEmptyWithNote()
    {
        cache.ClearCacheDirectory();
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

    [Fact]
    public void Trace_NotFound_ReturnsSearchHint()
    {
        cache.ClearCacheDirectory();
        SeedProject(_ => { });

        var result = new TracePathService().Trace(Project, "Missing");

        Assert.False(result.Found);
        Assert.Contains("search_graph", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Trace_AppliesRiskLabelsAndFiltersTests()
    {
        cache.ClearCacheDirectory();
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

    private static void SeedProject(Action<GbStore> seed)
    {
        var path = GbCachePaths.GetProjectDatabasePath(Project);
        using var store = GbStore.OpenPath(path);
        store.UpsertProject(Project, "/tmp/trace-service");
        seed(store);
    }

    private static GbNode MethodNode(
        string name,
        string qualifiedName,
        string filePath,
        int startLine,
        int endLine)
    {
        return new GbNode
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

    private static GbEdge Edge(long sourceId, long targetId, string type)
    {
        return new GbEdge
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
        };
    }
}
