using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Tests;

public class CbmStoreTests
{
    private const string Project = "test-project";

    [Fact]
    public void OpenMemoryCreatesSchema()
    {
        using var store = CbmStore.OpenMemory();

        store.UpsertProject(Project, "/repo");

        Assert.Equal(0, store.CountNodes(Project));
    }

    [Fact]
    public void UpsertNodeUpdatesExistingQualifiedName()
    {
        using var store = CreateStore();
        var firstId = store.UpsertNode(Node("Original", "Root.Type.Original", "src/original.cs"));
        var secondId = store.UpsertNode(Node("Renamed", "Root.Type.Original", "src/renamed.cs") with
        {
            StartLine = 7,
            EndLine = 9,
            PropertiesJson = """{"complexity":2}""",
        });

        var node = store.FindNodeByQualifiedName(Project, "Root.Type.Original");

        Assert.Equal(firstId, secondId);
        Assert.NotNull(node);
        Assert.Equal("Renamed", node.Name);
        Assert.Equal("src/renamed.cs", node.FilePath);
        Assert.Equal(7, node.StartLine);
        Assert.Equal("""{"complexity":2}""", node.PropertiesJson);
    }

    [Fact]
    public void BatchUpsertsNodesAndEdges()
    {
        using var store = CreateStore();
        var ids = store.UpsertNodeBatch(
        [
            Node("Caller", "Root.Caller", "src/caller.cs"),
            Node("Callee", "Root.Callee", "src/callee.cs"),
            Node("Other", "Root.Other", "src/other.cs"),
        ]);

        store.UpsertEdgeBatch(
        [
            Edge(ids[0], ids[1], "CALLS"),
            Edge(ids[2], ids[1], "CALLS"),
        ]);

        var calleeDegree = store.GetNodeDegree(ids[1]);
        var degrees = store.BatchCountDegrees(ids, "CALLS");

        Assert.Equal(2, calleeDegree.InDegree);
        Assert.Equal(0, calleeDegree.OutDegree);
        Assert.Equal(1, degrees[ids[0]].OutDegree);
        Assert.Equal(2, degrees[ids[1]].InDegree);
        Assert.Equal(1, degrees[ids[2]].OutDegree);
    }

    [Fact]
    public void SearchNodesUsesRegisteredRegexFunctions()
    {
        using var store = CreateStore();
        store.UpsertNode(Node("FindUser", "Root.FindUser", "src/find.cs"));

        var caseSensitiveMiss = store.SearchNodes(Project, namePattern: "^find", caseSensitive: true);
        var caseInsensitiveHit = store.SearchNodes(Project, namePattern: "^find", caseSensitive: false);

        Assert.Empty(caseSensitiveMiss);
        Assert.Single(caseInsensitiveHit);
        Assert.Equal("FindUser", caseInsensitiveHit[0].Name);
    }

    [Fact]
    public void SearchNodesMatchesCamelCaseFtsTokens()
    {
        using var store = CreateStore();
        store.UpsertNode(Node("updateCloudClient", "Root.Client.updateCloudClient", "src/client.cs"));

        var results = store.SearchNodes(Project, query: "cloud");

        Assert.Single(results);
        Assert.Equal("updateCloudClient", results[0].Name);
    }

    [Fact]
    public void ReadHelpersReturnSuffixDegreesNeighborsAndFiles()
    {
        using var store = CreateStore();
        var callerId = store.UpsertNode(Node("Caller", "Root.Feature.Caller", "src/feature/caller.cs"));
        var targetId = store.UpsertNode(Node("Target", "Root.Feature.Target", "src/feature/target.cs"));
        store.UpsertEdge(Edge(callerId, targetId, "CALLS"));

        var suffixMatches = store.FindNodesByQualifiedNameSuffix(Project, "Feature.Target");
        var degree = store.GetNodeDegree(targetId);
        var neighbors = store.GetNodeNeighborNames(targetId, limit: 10);
        var files = store.ListFiles(Project);

        Assert.Single(suffixMatches);
        Assert.Equal("Target", suffixMatches[0].Name);
        Assert.Equal(1, degree.InDegree);
        Assert.Equal(0, degree.OutDegree);
        Assert.Equal(["Caller"], neighbors.Callers);
        Assert.Empty(neighbors.Callees);
        Assert.Equal(["src/feature/caller.cs", "src/feature/target.cs"], files);
    }

    [Fact]
    public void DumpToFilePersistsRows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "cbm-store-tests", Guid.NewGuid() + ".db");

        using (var store = CreateStore())
        {
            store.UpsertNode(Node("Persisted", "Root.Persisted", "src/persisted.cs"));
            store.DumpToFile(dbPath);
        }

        using var reopened = CbmStore.OpenPath(dbPath);
        var node = reopened.FindNodeByQualifiedName(Project, "Root.Persisted");

        Assert.NotNull(node);
        Assert.Equal("Persisted", node.Name);
    }

    [Fact]
    public void BulkModeAndFileHashBatchComplete()
    {
        using var store = CreateStore();

        store.BeginBulk();
        store.UpsertFileHashBatch(
        [
            new CbmFileHash
            {
                Project = Project,
                RelativePath = "src/a.cs",
                Sha256 = "abc",
                MtimeNs = 1,
                Size = 10,
            },
        ]);
        var nodeId = store.UpsertNode(Node("BulkNode", "Root.BulkNode", "src/a.cs"));
        store.EndBulk();

        var hashes = store.GetFileHashes(Project);
        Assert.Single(hashes);
        Assert.Equal("src/a.cs", hashes[0].RelativePath);
        Assert.Equal("abc", hashes[0].Sha256);

        Assert.NotEqual(0, nodeId);
    }

    private static CbmStore CreateStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/repo");
        return store;
    }

    private static CbmNode Node(string name, string qualifiedName, string filePath)
    {
        return new CbmNode
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 3,
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
