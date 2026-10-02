using Gb.Graph;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbSearchCodeServiceTests : IClassFixture<GbTestCacheClassFixture>
{
    private const string Project = "search-code-service";

    private readonly GbTestCacheClassFixture cache;

    public GbSearchCodeServiceTests(GbTestCacheClassFixture cache)
    {
        this.cache = cache;
    }

    [Fact]
    public void Search_PutsUnmappedHitsInRawMatches()
    {
        cache.ClearCacheDirectory();
        using var temp = GbTestTempDirectory.Create("gb-search-code-orphan");
        GbTestSampleProjects.WriteFile(
            temp.Path,
            "Orphan.cs",
            """
            // orphan marker XYZ123
            """);

        SeedProject(temp.Path, store =>
        {
            store.UpsertNode(new GbNode
            {
                Project = Project,
                Label = "Method",
                Name = "Later",
                QualifiedName = $"{Project}.Orphan.Later",
                FilePath = "Orphan.cs",
                StartLine = 10,
                EndLine = 20,
            });
        });

        var result = new SearchCodeService().Search(Project, "orphan marker XYZ123");

        Assert.Equal(0, result.TotalResults);
        Assert.Equal(1, result.RawMatchCount);
        Assert.Contains(result.RawMatches, raw => raw.File == "Orphan.cs" && raw.Line == 1);
    }

    [Fact]
    public void Search_InvalidRegexPatternThrows()
    {
        cache.ClearCacheDirectory();
        SeedProject(null, store =>
        {
            store.UpsertNode(MethodNode("noop", $"{Project}.noop", "noop.cs", 1, 5));
        });

        var exception = Assert.Throws<ArgumentException>(() =>
            new SearchCodeService().Search(Project, "(unclosed", useRegex: true));

        Assert.Contains("invalid regex pattern", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_InvalidRootPathReportsRootPath()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/search;code-service", store =>
        {
            store.UpsertNode(MethodNode("noop", $"{Project}.noop", "noop.cs", 1, 5));
        });

        var exception = Assert.Throws<ArgumentException>(() =>
            new SearchCodeService().Search(Project, "noop"));

        Assert.Contains("root_path contains invalid characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_RanksHigherFanInMethodFirst()
    {
        cache.ClearCacheDirectory();
        using var temp = GbTestTempDirectory.Create("gb-search-code-rank");
        GbTestSampleProjects.WriteFile(
            temp.Path,
            "Rank.cs",
            """
            namespace Sample;

            public sealed class Rank
            {
                public void Popular() { }
                public void Obscure() { }
            }
            """);

        SeedProject(temp.Path, store =>
        {
            var popularId = store.UpsertNode(MethodNode("Popular", $"{Project}.Rank.Popular", "Rank.cs", 5, 5));
            var obscureId = store.UpsertNode(MethodNode("Obscure", $"{Project}.Rank.Obscure", "Rank.cs", 6, 6));
            store.UpsertEdge(new GbEdge { Project = Project, SourceId = popularId, TargetId = popularId, Type = "CALLS" });
            store.UpsertEdge(new GbEdge { Project = Project, SourceId = obscureId, TargetId = popularId, Type = "CALLS" });
        });

        var result = new SearchCodeService().Search(Project, "public void");

        Assert.True(result.TotalResults >= 2);
        Assert.Equal("Popular", result.Results[0].Node);
    }

    private static GbNode MethodNode(string name, string qualifiedName, string file, int start, int end)
    {
        return new GbNode
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = file,
            StartLine = start,
            EndLine = end,
        };
    }

    private static void SeedProject(string? rootPath, Action<GbStore> seed)
    {
        var path = GbCachePaths.GetProjectDatabasePath(Project);
        using var store = GbStore.OpenPath(path);
        store.UpsertProject(Project, rootPath ?? "/tmp/search-code-service");
        seed(store);
    }
}
