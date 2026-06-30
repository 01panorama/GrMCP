using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmAdrStoreTests
{
    private const string Project = "adr-store";

    [Fact]
    public void AdrStore_GetRoundTrip()
    {
        using var store = CreateStore();
        const string content = "## PURPOSE\nTest ADR content.\n";

        store.AdrStore(Project, content);
        var adr = store.AdrGet(Project);

        Assert.NotNull(adr);
        Assert.Equal(Project, adr.Project);
        Assert.Equal(content, adr.Content);
        Assert.False(string.IsNullOrWhiteSpace(adr.CreatedAt));
        Assert.False(string.IsNullOrWhiteSpace(adr.UpdatedAt));
        Assert.True(store.AdrExists(Project));
    }

    [Fact]
    public void AdrStore_UpsertPreservesCreatedAt()
    {
        using var store = CreateStore();
        store.AdrStore(Project, "## PURPOSE\nFirst.\n");
        var first = store.AdrGet(Project);
        Assert.NotNull(first);

        store.AdrStore(Project, "## PURPOSE\nSecond.\n");
        var second = store.AdrGet(Project);

        Assert.NotNull(second);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal("## PURPOSE\nSecond.\n", second.Content);
    }

    [Fact]
    public void AdrDelete_RemovesExistingRow()
    {
        using var store = CreateStore();
        store.AdrStore(Project, "## PURPOSE\nDelete me.\n");

        Assert.True(store.AdrDelete(Project));
        Assert.Null(store.AdrGet(Project));
        Assert.False(store.AdrExists(Project));
    }

    [Fact]
    public void AdrDelete_ReturnsFalseWhenMissing()
    {
        using var store = CreateStore();
        Assert.False(store.AdrDelete(Project));
    }

    [Fact]
    public void ParseSections_SplitsCanonicalHeadersOnly()
    {
        const string content =
            """
            preamble
            ## PURPOSE
            Foo
            ## CUSTOM
            Still in PURPOSE

            ## STACK
            Bar
            """;

        var sections = CbmAdrSections.ParseSections(content);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Foo\n## CUSTOM\nStill in PURPOSE", sections["PURPOSE"]);
        Assert.Equal("Bar", sections["STACK"]);
    }

    [Fact]
    public void RenderSections_OrdersCanonicalThenAlphabeticalExtras()
    {
        var sections = new Dictionary<string, string>
        {
            ["ZZZ"] = "extra",
            ["PURPOSE"] = "why",
            ["STACK"] = "tech",
        };

        var rendered = CbmAdrSections.RenderSections(sections);

        Assert.Contains("## PURPOSE\nwhy", rendered, StringComparison.Ordinal);
        Assert.True(rendered.IndexOf("## PURPOSE", StringComparison.Ordinal)
            < rendered.IndexOf("## STACK", StringComparison.Ordinal));
        Assert.True(rendered.IndexOf("## STACK", StringComparison.Ordinal)
            < rendered.IndexOf("## ZZZ", StringComparison.Ordinal));
    }

    [Fact]
    public void ListSectionHeaders_ReturnsHashPrefixedLines()
    {
        const string content =
            """
            # Title
            ## PURPOSE
            body
            ## STACK
            more
            """;

        var headers = CbmAdrSections.ListSectionHeaders(content);

        Assert.Equal(3, headers.Count);
        Assert.Equal("# Title", headers[0]);
        Assert.Equal("## PURPOSE", headers[1]);
        Assert.Equal("## STACK", headers[2]);
    }

    [Fact]
    public void AdrUpdateSections_MergesIntoExistingAdr()
    {
        using var store = CreateStore();
        store.AdrStore(Project, "## PURPOSE\nOriginal.\n\n## STACK\nGo.\n");

        var updated = store.AdrUpdateSections(
            Project,
            new Dictionary<string, string> { ["ARCHITECTURE"] = "Layered." });

        Assert.Contains("Original.", updated.Content, StringComparison.Ordinal);
        Assert.Contains("## ARCHITECTURE", updated.Content, StringComparison.Ordinal);
        Assert.Contains("Layered.", updated.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void AdrUpdateSections_RejectsMergedContentOverMaxLength()
    {
        using var store = CreateStore();
        store.AdrStore(Project, "## PURPOSE\nseed\n");

        var huge = new string('x', CbmAdrSections.MaxLength);
        Assert.Throws<InvalidOperationException>(() =>
            store.AdrUpdateSections(Project, new Dictionary<string, string> { ["STACK"] = huge }));
    }

    private static CbmStore CreateStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/repo/adr-store");
        return store;
    }
}
