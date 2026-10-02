using System.Text.Json;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbIngestTracesServiceTests : IClassFixture<GbTestCacheClassFixture>
{
    private const string Project = "ingest-traces-service";

    private readonly GbTestCacheClassFixture cache;

    public GbIngestTracesServiceTests(GbTestCacheClassFixture cache)
    {
        this.cache = cache;
    }

    [Fact]
    public void Ingest_EmptyArray_ReturnsAcceptedWithZeroCounts()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/ingest-empty");
        var result = new IngestTracesService().Ingest(Project, Array.Empty<JsonElement>());

        Assert.Equal("accepted", result.Status);
        Assert.Equal(0, result.TracesReceived);
        Assert.Equal(0, result.TracesIngested);
        Assert.Equal(0, result.EdgesMatched);
        Assert.Equal(0, result.Unresolved);
    }

    [Fact]
    public void Ingest_UnparseableEntry_AddsWarningAndSkipsIngest()
    {
        cache.ClearCacheDirectory();
        SeedProject("/tmp/ingest-partial");
        var result = new IngestTracesService().Ingest(
            Project,
            [ParseTrace("""{"service":"api-only"}""")]);

        Assert.Equal(1, result.TracesReceived);
        Assert.Equal(0, result.TracesIngested);
        Assert.Single(result.Warnings);
        Assert.Contains("caller, callee, or route", result.Warnings[0], StringComparison.Ordinal);
    }

    private static void SeedProject(string rootPath)
    {
        using var store = GbStore.OpenPath(GbCachePaths.GetProjectDatabasePath(Project));
        store.UpsertProject(Project, rootPath);
    }

    private static JsonElement ParseTrace(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
