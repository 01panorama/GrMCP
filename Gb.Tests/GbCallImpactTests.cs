using Gb.Pipeline;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbCallImpactTests : IClassFixture<IndexedCallerCalleeFixture>
{
    private readonly IndexedCallerCalleeFixture indexed;

    public GbCallImpactTests(IndexedCallerCalleeFixture indexed)
    {
        this.indexed = indexed;
    }

    [Fact]
    public void Propagate_FindsInboundCallerForChangedCalleeFile()
    {
        var impact = new CallImpactService().Propagate(
            indexed.ProjectName,
            ["Callee.cs"],
            depth: 2);

        Assert.True(impact.ChangedSymbolCount > 0);
        Assert.Contains(
            impact.ChangedSymbols,
            symbol => symbol.Name == "Target" && symbol.File == "Callee.cs");
        Assert.Contains(
            impact.ImpactedSymbols,
            symbol => symbol.Name == "Run" &&
                symbol.Direction == "inbound" &&
                symbol.Hop == 1);
    }

    [Fact]
    public void Propagate_FindsOutboundCalleeForChangedCallerFile()
    {
        var impact = new CallImpactService().Propagate(
            indexed.ProjectName,
            ["Caller.cs"],
            depth: 2);

        Assert.Contains(
            impact.ImpactedSymbols,
            symbol => symbol.Name == "Target" &&
                symbol.Direction == "outbound" &&
                symbol.Hop == 1);
    }
}
