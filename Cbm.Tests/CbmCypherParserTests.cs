using Cbm.Cypher;

namespace Cbm.Tests;

public sealed class CbmCypherParserTests
{
    [Fact]
    public void LexesKeywordsOperatorsAndLiterals()
    {
        var tokens = CypherParserFrontEnd.Lex(
            """
            MATCH (n:Method) WHERE n.name = "Foo" AND n.score >= 1.5 RETURN n
            """);

        Assert.Contains(tokens, token => token.Type == CypherTokenType.Match);
        Assert.Contains(tokens, token => token.Type == CypherTokenType.Where);
        Assert.Contains(tokens, token => token.Type == CypherTokenType.GreaterThanOrEqual);
        Assert.Contains(tokens, token => token.Type == CypherTokenType.String && token.Text == "Foo");
        Assert.Equal(CypherTokenType.Eof, tokens[^1].Type);
    }

    [Fact]
    public void ParsesSimpleMatchReturn()
    {
        var query = CypherParserFrontEnd.Parse("MATCH (n:Method) RETURN n.name");

        var pattern = query.Patterns[0];
        Assert.Single(query.Patterns);
        Assert.Equal("n", pattern.Nodes[0].Variable);
        Assert.Equal("Method", pattern.Nodes[0].Label);
        Assert.Null(query.Where);
        Assert.NotNull(query.Return);
        Assert.Equal("n", query.Return!.Items[0].Variable);
        Assert.Equal("name", query.Return.Items[0].Property);
    }

    [Fact]
    public void ParsesPathPatternWithHopRangeAndInlineProperties()
    {
        var query = CypherParserFrontEnd.Parse(
            """
            MATCH (n:Class)-[:DEFINES_METHOD|CALLS*1..3]->(m:Method {name: "Run"})
            RETURN n, m
            """);

        var pattern = query.Patterns[0];
        Assert.Equal(2, pattern.Nodes.Count);
        Assert.Single(pattern.Relationships);

        var relationship = pattern.Relationships[0];
        Assert.Equal(["DEFINES_METHOD", "CALLS"], relationship.Types);
        Assert.Equal(CypherRelDirection.Outbound, relationship.Direction);
        Assert.Equal(1, relationship.MinHops);
        Assert.Equal(3, relationship.MaxHops);

        Assert.Equal("m", pattern.Nodes[1].Variable);
        Assert.Equal("Method", pattern.Nodes[1].Label);
        Assert.Equal("name", pattern.Nodes[1].Properties[0].Key);
        Assert.Equal("Run", pattern.Nodes[1].Properties[0].Value);
        Assert.Equal(2, query.Return!.Items.Count);
    }

    [Fact]
    public void ParsesDeadCodeExistsPredicate()
    {
        var query = CypherParserFrontEnd.Parse(
            """
            MATCH (f:Method)
            WHERE NOT EXISTS { (f)<-[:CALLS]-() }
            RETURN f.qualified_name
            """);

        Assert.NotNull(query.Where?.Root);
        var condition = query.Where!.Root!;
        Assert.Equal(CypherExprKind.Not, condition.Kind);

        var exists = condition.Left!;
        Assert.Equal(CypherExprKind.Condition, exists.Kind);
        Assert.Equal("EXISTS", exists.Condition!.Operator);
        Assert.Equal("f", exists.Condition.Variable);
        Assert.Equal("CALLS", exists.Condition.Value);
        Assert.Equal(CypherExistsDirection.Inbound, exists.Condition.ExistsDirection);
        Assert.False(exists.Condition.Negated);
    }

    [Fact]
    public void ParsesBooleanWhereExpressions()
    {
        var query = CypherParserFrontEnd.Parse(
            """
            MATCH (n:Method)
            WHERE n.name =~ ".*Run.*" OR (n.cognitive > 80 AND n.is_test = false)
            RETURN n.name
            """);

        var root = query.Where!.Root!;
        Assert.Equal(CypherExprKind.Or, root.Kind);
        Assert.Equal("=~", root.Left!.Condition!.Operator);

        var andExpr = root.Right!;
        Assert.Equal(CypherExprKind.And, andExpr.Kind);
        Assert.Equal(">", andExpr.Left!.Condition!.Operator);
    }

    [Fact]
    public void ParsesReturnMetadataAndAggregate()
    {
        var query = CypherParserFrontEnd.Parse(
            """
            MATCH (n:Method)-[:CALLS]->(m)
            RETURN COUNT(m) AS cnt ORDER BY cnt DESC LIMIT 10
            """);

        var ret = query.Return!;
        Assert.Equal("COUNT", ret.Items[0].Function);
        Assert.Equal("cnt", ret.Items[0].Alias);
        Assert.Equal("cnt", ret.OrderBy);
        Assert.Equal("DESC", ret.OrderDirection);
        Assert.Equal(10, ret.Limit);
    }

    [Fact]
    public void ParsesUnionChain()
    {
        var query = CypherParserFrontEnd.Parse(
            """
            MATCH (n:Method) RETURN n.name
            UNION ALL
            MATCH (c:Class) RETURN c.name
            """);

        Assert.True(query.UnionAll);
        Assert.NotNull(query.UnionNext);
        Assert.False(query.UnionNext!.UnionAll);
        Assert.Equal("Class", query.UnionNext.Patterns[0].Nodes[0].Label);
    }

    [Fact]
    public void RejectsCallClause()
    {
        var exception = Assert.Throws<CypherParseException>(() =>
            CypherParserFrontEnd.Parse("CALL db.labels() YIELD label RETURN label"));

        Assert.Contains("CALL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsWriteClauses()
    {
        var exception = Assert.Throws<CypherParseException>(() =>
            CypherParserFrontEnd.Parse("CREATE (n:Method {name: \"x\"}) RETURN n"));

        Assert.Contains("CREATE", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedExistsPattern()
    {
        var exception = Assert.Throws<CypherParseException>(() =>
            CypherParserFrontEnd.Parse(
                """
                MATCH (f:Method)
                WHERE EXISTS { (f)<-[:CALLS*1..2]-() }
                RETURN f
                """));

        Assert.Contains("EXISTS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
