using System.Text.Json;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbIngestTracesIndexedTests : IClassFixture<IndexedCallerCalleeFixture>
{
    private readonly IndexedCallerCalleeFixture indexed;

    public GbIngestTracesIndexedTests(IndexedCallerCalleeFixture indexed)
    {
        this.indexed = indexed;
    }

    [Fact]
    public void Ingest_MatchesCallerCalleeAgainstIndexedGraph()
    {
        var service = new IngestTracesService();

        var result = service.Ingest(
            indexed.ProjectName,
            [ParseTrace("""{"caller":"Run","callee":"Target","duration_ms":12.5,"count":2}""")]);

        Assert.Equal("accepted", result.Status);
        Assert.Equal(1, result.TracesReceived);
        Assert.Equal(1, result.TracesIngested);
        Assert.Equal(1, result.EdgesMatched);
        Assert.Equal(0, result.Unresolved);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Ingest_AcceptsOtlpLikeHttpSpan()
    {
        await indexed.ReindexAsync();
        var service = new IngestTracesService();

        var result = service.Ingest(
            indexed.ProjectName,
            [ParseTrace(
                """
                {
                  "resource": {
                    "attributes": [
                      { "key": "service.name", "string_value": "orders-api" }
                    ]
                  },
                  "attributes": [
                    { "key": "http.method", "string_value": "GET" },
                    { "key": "http.route", "string_value": "/orders" },
                    { "key": "http.status_code", "string_value": "200" }
                  ],
                  "start_time": "1000000000",
                  "end_time": "1500000000"
                }
                """)]);

        Assert.Equal(1, result.TracesIngested);
        Assert.Equal(0, result.Unresolved);

        using var store = GbStore.OpenPath(GbCachePaths.GetProjectDatabasePath(indexed.ProjectName));
        var observations = store.ListRuntimeObservations(indexed.ProjectName, limit: 1);
        Assert.Single(observations);
        Assert.Equal("orders-api", observations[0].Service);
        Assert.Equal("/orders", observations[0].Route);
        Assert.Equal("GET", observations[0].Method);
    }

    [Fact]
    public async Task Ingest_UnresolvedSymbolsStillPersist()
    {
        await indexed.ReindexAsync();
        var service = new IngestTracesService();

        var result = service.Ingest(
            indexed.ProjectName,
            [ParseTrace("""{"caller":"Missing","callee":"AlsoMissing","count":1}""")]);

        Assert.Equal(1, result.TracesIngested);
        Assert.Equal(1, result.Unresolved);
        Assert.Equal(0, result.EdgesMatched);

        using var store = GbStore.OpenPath(GbCachePaths.GetProjectDatabasePath(indexed.ProjectName));
        Assert.Equal(1, store.CountRuntimeObservations(indexed.ProjectName));
    }

    private static JsonElement ParseTrace(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
