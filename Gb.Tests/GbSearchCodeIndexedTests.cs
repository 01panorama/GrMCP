using Gb.Pipeline;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbSearchCodeIndexedTests : IClassFixture<IndexedCallerCalleeWithWorkerFixture>
{
    private readonly IndexedCallerCalleeWithWorkerFixture indexed;

    public GbSearchCodeIndexedTests(IndexedCallerCalleeWithWorkerFixture indexed)
    {
        this.indexed = indexed;
    }

    [Fact]
    public void Search_DedupesLiteralPatternToContainingMethod()
    {
        var result = new SearchCodeService().Search(indexed.ProjectName, "Execute");

        Assert.True(result.TotalResults >= 1);
        var hit = result.Results[0];
        Assert.Equal("Execute", hit.Node);
        Assert.Equal("Method", hit.Label);
        Assert.Contains(hit.MatchLines, line => line > 0);
    }

    [Fact]
    public void Search_FilesModeReturnsDistinctPaths()
    {
        var result = new SearchCodeService().Search(
            indexed.ProjectName,
            "Target",
            mode: "files");

        Assert.NotNull(result.Files);
        Assert.Contains(result.Files, file => file.EndsWith("Callee.cs", StringComparison.Ordinal));
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Search_FilePatternAndPathFilterNarrowResults()
    {
        var filtered = new SearchCodeService().Search(
            indexed.ProjectName,
            "Target",
            filePattern: "Callee.cs",
            pathFilter: "^Callee\\.cs$");

        Assert.Equal(1, filtered.TotalResults);
        Assert.Equal("Callee.cs", filtered.Results[0].File);
    }
}
