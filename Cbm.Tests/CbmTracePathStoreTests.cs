using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmTracePathStoreTests
{
    private const string Project = "trace-store";

    [Fact]
    public void FindNodesByName_ReturnsExactNameMatches()
    {
        using var store = CreateStore();
        store.UpsertNode(MethodNode("Foo", "trace-store.a.Foo", "a.cs", 1, 5));
        store.UpsertNode(MethodNode("Foo", "trace-store.b.Foo", "b.cs", 1, 5));

        var matches = store.FindNodesByName(Project, "Foo");

        Assert.Equal(2, matches.Count);
        Assert.All(matches, node => Assert.Equal("Foo", node.Name));
    }

    [Fact]
    public void Bfs_Outbound_RespectsDepth()
    {
        using var store = CreateStore();
        var ids = InsertChain(store, depth: 4);

        var shallow = store.Bfs(ids[0], "outbound", ["CALLS"], maxDepth: 1);
        var deep = store.Bfs(ids[0], "outbound", ["CALLS"], maxDepth: 3);

        Assert.Equal("A", shallow.Root.Name);
        Assert.Single(shallow.Visited);
        Assert.Equal("B", shallow.Visited[0].Node.Name);
        Assert.Equal(1, shallow.Visited[0].Hop);

        Assert.True(deep.Visited.Count >= 3);
        Assert.Contains(deep.Visited, hop => hop.Node.Name == "D");
    }

    [Fact]
    public void Bfs_Inbound_FindsMultipleCallers()
    {
        using var store = CreateStore();
        var idA = store.UpsertNode(MethodNode("A", "trace-store.A", "a.cs", 1, 5));
        var idB = store.UpsertNode(MethodNode("B", "trace-store.B", "b.cs", 1, 5));
        var idC = store.UpsertNode(MethodNode("C", "trace-store.C", "c.cs", 1, 5));
        store.UpsertEdge(Edge(idA, idC, "CALLS"));
        store.UpsertEdge(Edge(idB, idC, "CALLS"));

        var inbound = store.Bfs(idC, "inbound", ["CALLS"], maxDepth: 3);

        Assert.Equal(2, inbound.Visited.Count);
        Assert.Contains(inbound.Visited, hop => hop.Node.Name == "A");
        Assert.Contains(inbound.Visited, hop => hop.Node.Name == "B");
    }

    [Fact]
    public void Bfs_DataFlow_FollowsUsageAndWritesEdges()
    {
        using var store = CreateStore();
        var idA = store.UpsertNode(MethodNode("A", "trace-store.A", "a.cs", 1, 5));
        var idB = store.UpsertNode(MethodNode("B", "trace-store.B", "b.cs", 1, 5));
        store.UpsertEdge(Edge(idA, idB, "USAGE"));

        var callsOnly = store.Bfs(idA, "outbound", ["CALLS"], maxDepth: 2);
        var dataFlow = store.Bfs(idA, "outbound", ["CALLS", "USAGE", "WRITES"], maxDepth: 2);

        Assert.Empty(callsOnly.Visited);
        Assert.Single(dataFlow.Visited);
        Assert.Equal("B", dataFlow.Visited[0].Node.Name);
    }

    [Theory]
    [InlineData(1, "CRITICAL")]
    [InlineData(2, "HIGH")]
    [InlineData(3, "MEDIUM")]
    [InlineData(4, "LOW")]
    [InlineData(10, "LOW")]
    public void HopToRiskLabel_MapsHopDistance(int hop, string expected)
    {
        Assert.Equal(expected, CbmStore.HopToRiskLabel(hop));
    }

    private static CbmStore CreateStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/trace-store");
        return store;
    }

    private static long[] InsertChain(CbmStore store, int depth)
    {
        var names = new[] { "A", "B", "C", "D", "E" };
        var ids = new long[depth + 1];
        for (var i = 0; i <= depth; i++)
        {
            ids[i] = store.UpsertNode(MethodNode(names[i], $"trace-store.{names[i]}", $"{names[i]}.cs", 1, 5));
        }

        for (var i = 0; i < depth; i++)
        {
            store.UpsertEdge(Edge(ids[i], ids[i + 1], "CALLS"));
        }

        return ids;
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
}
