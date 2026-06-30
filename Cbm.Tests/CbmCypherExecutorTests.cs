using Cbm.Cypher;
using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmCypherExecutorTests
{
    private const string Project = "test";

    [Fact]
    public void ExecutesDeadCodeExistsQuery()
    {
        using var store = SetupCypherStore();

        var result = CypherExecutor.Execute(
            store,
            """
            MATCH (f:Method)
            WHERE NOT EXISTS { (f)<-[:CALLS]-() }
            RETURN f.name
            """,
            Project);

        Assert.Single(result.Rows);
        Assert.Equal("HandleOrder", result.Rows[0][0]);
        Assert.Null(result.Hint);
    }

    [Fact]
    public void ExecutesComplexityHotspotQuery()
    {
        using var store = SetupCypherStore();

        var result = CypherExecutor.Execute(
            store,
            """
            MATCH (n:Method)
            WHERE n.cognitive > 80
            RETURN n.qualified_name, n.cognitive
            ORDER BY n.cognitive DESC
            LIMIT 10
            """,
            Project);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("test.HandleOrder", result.Rows[0][0]);
        Assert.Equal("90", result.Rows[0][1]);
        Assert.Equal("test.LogError", result.Rows[1][0]);
        Assert.Equal("85", result.Rows[1][1]);
    }

    [Fact]
    public void ExecutesAggregateGroupingByCaller()
    {
        using var store = SetupCypherStore();

        var result = CypherExecutor.Execute(
            store,
            """
            MATCH (f:Method)-[:CALLS]->(g:Method)
            RETURN f.name, COUNT(g) AS cnt
            """,
            Project);

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(
            result.Rows,
            row => row[0] == "HandleOrder" && row[1] == "2");
        Assert.Contains(
            result.Rows,
            row => row[0] == "ValidateOrder" && row[1] == "1");
    }

    [Fact]
    public void ExecutesCallsRelationshipPath()
    {
        using var store = SetupCypherStore();

        var result = CypherExecutor.Execute(
            store,
            """
            MATCH (f:Method)-[:CALLS]->(g:Method)
            RETURN f.name, g.name
            """,
            Project);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void ReturnsHintWhenQueryHasNoRows()
    {
        using var store = SetupCypherStore();

        var result = CypherExecutor.Execute(
            store,
            """
            MATCH (f:Method)
            WHERE f.name = "Missing"
            RETURN f.name
            """,
            Project);

        Assert.Empty(result.Rows);
        Assert.NotNull(result.Hint);
        Assert.Contains("get_graph_schema", result.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidCypher()
    {
        using var store = SetupCypherStore();

        Assert.Throws<CypherParseException>(() =>
            CypherExecutor.Execute(store, "INVALID QUERY", Project));
    }

    private static CbmStore SetupCypherStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");

        var handleOrderId = store.UpsertNode(Method(
            "HandleOrder",
            "test.HandleOrder",
            "handler.cs",
            cognitive: 90,
            complexity: 12));
        var validateOrderId = store.UpsertNode(Method(
            "ValidateOrder",
            "test.ValidateOrder",
            "validate.cs",
            cognitive: 50,
            complexity: 6));
        var submitOrderId = store.UpsertNode(Method(
            "SubmitOrder",
            "test.SubmitOrder",
            "submit.cs",
            cognitive: 30,
            complexity: 3));
        var mainId = store.UpsertNode(new CbmNode
        {
            Project = Project,
            Label = "Namespace",
            Name = "main",
            QualifiedName = "test.main",
            FilePath = "main.cs",
        });
        var logErrorId = store.UpsertNode(Method(
            "LogError",
            "test.LogError",
            "log.cs",
            cognitive: 85,
            complexity: 8));

        store.UpsertEdgeBatch(
        [
            Edge(handleOrderId, validateOrderId, "CALLS"),
            Edge(validateOrderId, submitOrderId, "CALLS"),
            Edge(handleOrderId, logErrorId, "CALLS"),
            Edge(mainId, handleOrderId, "DEFINES"),
        ]);

        return store;
    }

    private static CbmNode Method(
        string name,
        string qualifiedName,
        string filePath,
        int cognitive,
        int complexity) =>
        new()
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            PropertiesJson = $$"""{"cognitive":{{cognitive}},"complexity":{{complexity}}}""",
        };

    private static CbmEdge Edge(long sourceId, long targetId, string type) =>
        new()
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
        };
}
