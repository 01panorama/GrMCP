using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbManageAdrServiceTests : IClassFixture<GbTestCacheClassFixture>
{
    private const string Project = "manage-adr-service";

    private readonly GbTestCacheClassFixture cache;

    public GbManageAdrServiceTests(GbTestCacheClassFixture cache)
    {
        this.cache = cache;
    }

    [Fact]
    public void Manage_GetOnEmptyProject_ReturnsNoAdrHint()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/manage-adr-empty");

        var result = new ManageAdrService().Manage(Project);

        Assert.Equal(string.Empty, result.Content);
        Assert.Equal("no_adr", result.Status);
        Assert.Contains("No ADR yet", result.AdrHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Manage_UpdateAndGetRoundTrip()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/manage-adr-roundtrip");
        const string content = "## PURPOSE\nUnified ADR backend.\n";
        var service = new ManageAdrService();

        var updated = service.Manage(Project, mode: "update", content: content);
        var fetched = service.Manage(Project, mode: "get");

        Assert.Equal("updated", updated.Status);
        Assert.Equal(content, fetched.Content);
    }

    [Fact]
    public void Manage_SectionsListsStoredHeaders()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/manage-adr-sections");
        var service = new ManageAdrService();
        service.Manage(
            Project,
            mode: "update",
            content: "## PURPOSE\nWhy.\n\n## STACK\nC# + SQLite.\n");

        var result = service.Manage(Project, mode: "sections");

        Assert.NotNull(result.Sections);
        Assert.Contains("## PURPOSE", result.Sections);
        Assert.Contains("## STACK", result.Sections);
    }

    [Fact]
    public void Manage_ImportsLegacyAdrFileOnFirstAccess()
    {
        cache.ClearCacheDirectory();
        using var temp = GbTestTempDirectory.Create("gb-manage-adr-legacy");
        GbTestSampleProjects.WriteFile(
            temp.Path,
            ".graph-mcp/adr.md",
            """
            ## PURPOSE
            Legacy ADR content.

            ## STACK
            Markdown file.
            """);

        SeedProject(temp.Path);

        var result = new ManageAdrService().Manage(Project, mode: "get");

        Assert.Contains("Legacy ADR content.", result.Content, StringComparison.Ordinal);

        using var store = GbStore.OpenPath(GbCachePaths.GetProjectDatabasePath(Project));
        Assert.True(store.AdrExists(Project));
    }

    [Fact]
    public void Manage_UpdateWithoutContentFallsThroughToGet()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/manage-adr-fallthrough");
        var service = new ManageAdrService();
        service.Manage(Project, mode: "update", content: "## PURPOSE\nStored.\n");

        var result = service.Manage(Project, mode: "update", content: null);

        Assert.Equal("## PURPOSE\nStored.\n", result.Content);
    }

    [Fact]
    public void Manage_UnknownProject_Throws()
    {
        cache.ClearCacheDirectory();
        Assert.Throws<InvalidOperationException>(() =>
            new ManageAdrService().Manage("missing-project"));
    }

    private static void SeedProject(string rootPath)
    {
        var path = GbCachePaths.GetProjectDatabasePath(Project);
        using var store = GbStore.OpenPath(path);
        store.UpsertProject(Project, rootPath);
    }
}
