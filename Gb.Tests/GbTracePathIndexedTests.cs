using Gb.Pipeline;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbTracePathIndexedTests : IClassFixture<IndexedCallerCalleeFixture>
{
    private readonly IndexedCallerCalleeFixture indexed;

    public GbTracePathIndexedTests(IndexedCallerCalleeFixture indexed)
    {
        this.indexed = indexed;
    }

    [Fact]
    public void Trace_FindsInboundAndOutboundCallersForIndexedFixture()
    {
        var service = new TracePathService();

        var inbound = service.Trace(indexed.ProjectName, "Target", direction: "inbound");
        var outbound = service.Trace(indexed.ProjectName, "Run", direction: "outbound");

        Assert.True(inbound.Found);
        Assert.Contains(inbound.Callers!, hop => hop.Name == "Run");
        Assert.True(outbound.Found);
        Assert.Contains(outbound.Callees!, hop => hop.Name == "Target");
    }
}
