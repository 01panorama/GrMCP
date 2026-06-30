using Cbm.Cypher;

namespace Cbm.Tests;

public sealed class CbmCypherSqlPlannerTests
{
    private const string Project = "fixture-project";

    [Fact]
    public void PlansFixedLengthRelationshipPath()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Method)-[:CALLS]->(m:Method)
            RETURN n.name, m.qualified_name
            """,
            Project);

        Assert.Contains("FROM nodes n INNER JOIN edges e0 ON e0.project = $p0", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("e0.source_id = n.id AND e0.target_id = m.id", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("e0.type = $p2", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN nodes m ON m.id = e0.target_id", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE n.project = $p0 AND n.label = $p1", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("m.project = $p0 AND m.label = $p3", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT n.name AS n_name, m.qualified_name AS m_qualified_name", plan.Sql, StringComparison.Ordinal);

        Assert.Equal(Project, plan.Parameters[0].Value);
        Assert.Equal("Method", plan.Parameters[1].Value);
        Assert.Equal("CALLS", plan.Parameters[2].Value);
        Assert.Equal("Method", plan.Parameters[3].Value);
    }

    [Fact]
    public void PlansScalarAndJsonPropertyFilters()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Method {name: "Run"})
            WHERE n.cognitive > 80 AND n.file_path CONTAINS "Tests"
            RETURN n.name
            """,
            Project);

        Assert.Contains("n.name = $p2", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("json_extract(n.properties, $p3) > $p4", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("INSTR(n.file_path, $p5) > 0", plan.Sql, StringComparison.Ordinal);

        Assert.Equal("Run", plan.Parameters[2].Value);
        Assert.Equal("$.cognitive", plan.Parameters[3].Value);
        Assert.Equal(80L, plan.Parameters[4].Value);
        Assert.Equal("Tests", plan.Parameters[5].Value);
    }

    [Fact]
    public void PlansVariableLengthRelationshipWithRecursiveCte()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Class)-[:DEFINES_METHOD|CALLS*1..3]->(m:Method {name: "Run"})
            RETURN n, m
            """,
            Project);

        Assert.Contains("WITH RECURSIVE reach_0(root_id, end_id, depth) AS (", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("reach_0 reach_0_r ON reach_0_r.root_id = n.id", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("reach_0_r.depth >= $", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("reach_0_r.depth <= $", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("e.type IN ($", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("m.name = $", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT n.* AS n, m.* AS m", plan.Sql, StringComparison.Ordinal);

        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, "DEFINES_METHOD"));
        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, "CALLS"));
        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, "Run"));
        Assert.Contains(plan.Parameters, parameter => parameter.Value is int depth && depth == 1);
        Assert.Contains(plan.Parameters, parameter => parameter.Value is int depth && depth == 3);
    }

    [Fact]
    public void PlansDeadCodeExistsPredicate()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (f:Method)
            WHERE NOT EXISTS { (f)<-[:CALLS]-() }
            RETURN f.qualified_name
            """,
            Project);

        Assert.Contains("(NOT EXISTS (SELECT 1 FROM edges ex_f WHERE ex_f.project = $p0", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("ex_f.target_id = f.id", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("ex_f.type = $", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT f.qualified_name AS f_qualified_name", plan.Sql, StringComparison.Ordinal);

        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, "CALLS"));
    }

    [Fact]
    public void PlansUnionWithRenumberedParameters()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Method) RETURN n.name
            UNION ALL
            MATCH (c:Class) RETURN c.name
            """,
            Project);

        Assert.Contains(" UNION ALL ", plan.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("$p0", plan.Sql[(plan.Sql.IndexOf("UNION ALL", StringComparison.Ordinal) + "UNION ALL".Length)..], StringComparison.Ordinal);

        var parameterNames = plan.Parameters.Select(parameter => parameter.Name).ToList();
        Assert.Equal(parameterNames, parameterNames.Distinct(StringComparer.Ordinal));
        Assert.Equal(Project, plan.Parameters[0].Value);
        Assert.Equal("Method", plan.Parameters[1].Value);
        Assert.Equal(Project, plan.Parameters[2].Value);
        Assert.Equal("Class", plan.Parameters[3].Value);
    }

    [Fact]
    public void PlansAggregateReturnWithGroupBy()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Method)-[:CALLS]->(m)
            RETURN n.label, COUNT(m) AS cnt ORDER BY cnt DESC LIMIT 10
            """,
            Project);

        Assert.Contains("SELECT n.label AS n_label, COUNT(m.id) AS cnt", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY n.label", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY cnt DESC", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 10", plan.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PlansAggregateReturnWithWholeNodeGroupBy()
    {
        var plan = CypherSqlPlanner.Plan(
            """
            MATCH (n:Method)-[:CALLS]->(m)
            RETURN n, COUNT(m) AS cnt
            """,
            Project);

        Assert.Contains("SELECT n.* AS n, COUNT(m.id) AS cnt", plan.Sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY n.id", plan.Sql, StringComparison.Ordinal);
    }
}
