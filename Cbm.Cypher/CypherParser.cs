namespace Cbm.Cypher;

public static class CypherParserFrontEnd
{
    public static IReadOnlyList<CypherToken> Lex(string query) => CypherLexer.Lex(query);

    public static CypherQuery Parse(string query)
    {
        var tokens = CypherLexer.Lex(query);
        return new CypherParser(tokens).ParseQuery();
    }
}

internal sealed class CypherParser
{
    private readonly IReadOnlyList<CypherToken> tokens;
    private int position;

    public CypherParser(IReadOnlyList<CypherToken> tokens)
    {
        this.tokens = tokens;
    }

    public CypherQuery ParseQuery()
    {
        EnsureNotUnsupportedClause(Peek().Type, "query");

        var patterns = new List<CypherPattern>();
        var patternOptional = new List<bool>();

        if (Match(CypherTokenType.Optional))
        {
            throw Unsupported("OPTIONAL MATCH is not supported in this milestone");
        }

        Expect(CypherTokenType.Match);
        patterns.Add(ParseMatchPattern());
        patternOptional.Add(false);

        while (Peek().Type is CypherTokenType.Match or CypherTokenType.Optional)
        {
            var optional = Match(CypherTokenType.Optional);
            Expect(CypherTokenType.Match);
            patterns.Add(ParseMatchPattern());
            patternOptional.Add(optional);
        }

        var where = ParseWhere();
        EnsureNotUnsupportedClause(Peek().Type, "clause");

        var ret = ParseReturn();
        CypherQuery? unionNext = null;
        var unionAll = false;

        if (Match(CypherTokenType.Union))
        {
            unionAll = Match(CypherTokenType.All);
            unionNext = ParseQuery();
        }

        return new CypherQuery(
            patterns,
            patternOptional,
            where,
            ret,
            unionNext,
            unionAll);
    }

    private CypherWhereClause? ParseWhere()
    {
        if (!Match(CypherTokenType.Where))
        {
            return null;
        }

        var root = ParseOrExpr();
        return new CypherWhereClause(root);
    }

    private CypherReturnClause? ParseReturn()
    {
        if (!Match(CypherTokenType.Return))
        {
            return null;
        }

        var distinct = Match(CypherTokenType.Distinct);
        var items = new List<CypherReturnItem>();
        var star = false;

        if (Match(CypherTokenType.Star))
        {
            star = true;
        }
        else
        {
            do
            {
                items.Add(ParseReturnItem());
            }
            while (Match(CypherTokenType.Comma));
        }

        string? orderBy = null;
        string? orderDirection = null;
        if (Match(CypherTokenType.Order))
        {
            Expect(CypherTokenType.By);
            orderBy = ParseOrderByExpression();
            if (Match(CypherTokenType.Asc))
            {
                orderDirection = "ASC";
            }
            else if (Match(CypherTokenType.Desc))
            {
                orderDirection = "DESC";
            }
        }

        var skip = 0;
        if (Match(CypherTokenType.Skip))
        {
            skip = ParseIntegerLiteral("SKIP");
        }

        var limit = 0;
        if (Match(CypherTokenType.Limit))
        {
            limit = ParseIntegerLiteral("LIMIT");
        }

        return new CypherReturnClause(items, distinct, star, orderBy, orderDirection, skip, limit);
    }

    private CypherReturnItem ParseReturnItem()
    {
        if (IsAggregateToken(Peek().Type))
        {
            return ParseAggregateItem();
        }

        if (IsStringFunctionToken(Peek().Type))
        {
            return ParseStringFunctionItem();
        }

        var variable = Expect(CypherTokenType.Ident).Text;
        string? property = null;

        if (Match(CypherTokenType.Dot))
        {
            property = Expect(CypherTokenType.Ident).Text;
        }

        if (Peek().Type is CypherTokenType.LeftParen or CypherTokenType.LeftBracket)
        {
            throw Unsupported(
                Peek().Type == CypherTokenType.LeftParen
                    ? $"unsupported function '{variable}'"
                    : "unsupported expression: list indexing/slicing '[...]' is not supported");
        }

        string? alias = null;
        if (Match(CypherTokenType.As))
        {
            alias = Expect(CypherTokenType.Ident).Text;
        }

        return new CypherReturnItem(variable, property, alias, null, false, []);
    }

    private CypherReturnItem ParseAggregateItem()
    {
        var function = AggregateFunctionName(Peek().Type);
        Advance();
        Expect(CypherTokenType.LeftParen);

        var distinct = Match(CypherTokenType.Distinct);
        string? variable;
        string? property = null;

        if (Match(CypherTokenType.Star))
        {
            variable = "*";
        }
        else
        {
            variable = Expect(CypherTokenType.Ident).Text;
            if (Match(CypherTokenType.Dot))
            {
                property = Expect(CypherTokenType.Ident).Text;
            }
        }

        Expect(CypherTokenType.RightParen);

        string? alias = null;
        if (Match(CypherTokenType.As))
        {
            alias = Expect(CypherTokenType.Ident).Text;
        }

        return new CypherReturnItem(variable, property, alias, function, distinct, []);
    }

    private CypherReturnItem ParseStringFunctionItem()
    {
        var function = StringFunctionName(Peek().Type);
        Advance();
        Expect(CypherTokenType.LeftParen);

        var variable = Expect(CypherTokenType.Ident).Text;
        string? property = null;
        if (Match(CypherTokenType.Dot))
        {
            property = Expect(CypherTokenType.Ident).Text;
        }

        Expect(CypherTokenType.RightParen);

        string? alias = null;
        if (Match(CypherTokenType.As))
        {
            alias = Expect(CypherTokenType.Ident).Text;
        }

        return new CypherReturnItem(variable, property, alias, function, false, []);
    }

    private string ParseOrderByExpression()
    {
        if (IsAggregateToken(Peek().Type))
        {
            var function = AggregateFunctionName(Peek().Type);
            Advance();
            Expect(CypherTokenType.LeftParen);
            var argument = Match(CypherTokenType.Star)
                ? "*"
                : Expect(CypherTokenType.Ident).Text;
            Expect(CypherTokenType.RightParen);
            return $"{function}({argument})";
        }

        var variable = Expect(CypherTokenType.Ident).Text;
        if (Match(CypherTokenType.Dot))
        {
            var property = Expect(CypherTokenType.Ident).Text;
            return $"{variable}.{property}";
        }

        return variable;
    }

    private CypherPattern ParseMatchPattern()
    {
        var nodes = new List<CypherNodePattern> { ParseNode() };
        var relationships = new List<CypherRelPattern>();

        while (Peek().Type is CypherTokenType.Dash or CypherTokenType.LessThan)
        {
            relationships.Add(ParseRelationship());
            nodes.Add(ParseNode());
        }

        return new CypherPattern(nodes, relationships);
    }

    private CypherNodePattern ParseNode()
    {
        Expect(CypherTokenType.LeftParen);

        string? variable = null;
        if (Peek().Type == CypherTokenType.Ident)
        {
            variable = Advance().Text;
        }

        string? label = null;
        if (Match(CypherTokenType.Colon))
        {
            label = Expect(CypherTokenType.Ident).Text;
            while (Match(CypherTokenType.Pipe))
            {
                label += "|" + Expect(CypherTokenType.Ident).Text;
            }
        }

        var properties = ParseInlineProperties();
        Expect(CypherTokenType.RightParen);

        return new CypherNodePattern(variable, label, properties);
    }

    private IReadOnlyList<CypherPropFilter> ParseInlineProperties()
    {
        if (!Match(CypherTokenType.LeftBrace))
        {
            return [];
        }

        var properties = new List<CypherPropFilter>();
        while (Peek().Type != CypherTokenType.RightBrace && Peek().Type != CypherTokenType.Eof)
        {
            var key = Expect(CypherTokenType.Ident).Text;
            Expect(CypherTokenType.Colon);
            var value = ParsePropertyValue();
            properties.Add(new CypherPropFilter(key, value));
            Match(CypherTokenType.Comma);
        }

        Expect(CypherTokenType.RightBrace);
        return properties;
    }

    private static string ParsePropertyValueFromToken(CypherToken token) =>
        token.Type switch
        {
            CypherTokenType.String => token.Text,
            CypherTokenType.Number => token.Text,
            CypherTokenType.True => "true",
            CypherTokenType.False => "false",
            _ => throw new CypherParseException($"expected property value, got {token.Type}", token.Position),
        };

    private string ParsePropertyValue()
    {
        var token = Peek();
        if (token.Type is CypherTokenType.String or CypherTokenType.Number or CypherTokenType.True or CypherTokenType.False)
        {
            Advance();
            return ParsePropertyValueFromToken(token);
        }

        throw new CypherParseException($"expected property value, got {token.Type}", token.Position);
    }

    private CypherRelPattern ParseRelationship()
    {
        var leadingLessThan = Match(CypherTokenType.LessThan);
        Expect(CypherTokenType.Dash);

        string? variable = null;
        IReadOnlyList<string> types = [];
        var minHops = 1;
        var maxHops = 1;

        if (Match(CypherTokenType.LeftBracket))
        {
            if (Peek().Type == CypherTokenType.Ident && PeekAhead(1)?.Type != CypherTokenType.Colon)
            {
                variable = Advance().Text;
            }

            if (Match(CypherTokenType.Colon))
            {
                types = ParseRelationshipTypes();
            }

            if (Match(CypherTokenType.Star))
            {
                (minHops, maxHops) = ParseHopRange();
            }

            Expect(CypherTokenType.RightBracket);
        }

        Expect(CypherTokenType.Dash);
        var trailingGreaterThan = Match(CypherTokenType.GreaterThan);

        var direction = leadingLessThan && !trailingGreaterThan
            ? CypherRelDirection.Inbound
            : !leadingLessThan && trailingGreaterThan
                ? CypherRelDirection.Outbound
                : CypherRelDirection.Any;

        return new CypherRelPattern(variable, types, direction, minHops, maxHops);
    }

    private IReadOnlyList<string> ParseRelationshipTypes()
    {
        var types = new List<string> { Expect(CypherTokenType.Ident).Text };
        while (Match(CypherTokenType.Pipe))
        {
            types.Add(Expect(CypherTokenType.Ident).Text);
        }

        return types;
    }

    private (int MinHops, int MaxHops) ParseHopRange()
    {
        if (Peek().Type == CypherTokenType.Number)
        {
            var value = int.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture);
            if (Match(CypherTokenType.DotDot))
            {
                var max = Peek().Type == CypherTokenType.Number
                    ? int.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture)
                    : 0;
                return (value, max);
            }

            return (1, value);
        }

        if (Match(CypherTokenType.DotDot))
        {
            var max = Peek().Type == CypherTokenType.Number
                ? int.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            return (1, max);
        }

        return (1, 0);
    }

    private CypherExpr ParseOrExpr()
    {
        var left = ParseXorExpr();
        while (Match(CypherTokenType.Or))
        {
            left = new CypherExpr(CypherExprKind.Or, Left: left, Right: ParseXorExpr());
        }

        return left;
    }

    private CypherExpr ParseXorExpr()
    {
        var left = ParseAndExpr();
        while (Match(CypherTokenType.Xor))
        {
            left = new CypherExpr(CypherExprKind.Xor, Left: left, Right: ParseAndExpr());
        }

        return left;
    }

    private CypherExpr ParseAndExpr()
    {
        var left = ParseNotExpr();
        while (Match(CypherTokenType.And))
        {
            left = new CypherExpr(CypherExprKind.And, Left: left, Right: ParseNotExpr());
        }

        return left;
    }

    private CypherExpr ParseNotExpr()
    {
        if (Match(CypherTokenType.Not))
        {
            return new CypherExpr(CypherExprKind.Not, Left: ParseNotExpr());
        }

        return ParseAtomExpr();
    }

    private CypherExpr ParseAtomExpr()
    {
        if (Match(CypherTokenType.LeftParen))
        {
            var expr = ParseOrExpr();
            Expect(CypherTokenType.RightParen);
            return expr;
        }

        return ParseConditionExpr();
    }

    private CypherExpr ParseConditionExpr()
    {
        var negated = Match(CypherTokenType.Not);
        if (Peek().Type == CypherTokenType.Exists)
        {
            return ParseExistsPredicate(negated);
        }

        var variable = Expect(CypherTokenType.Ident).Text;

        if (Peek().Type == CypherTokenType.Colon)
        {
            Advance();
            var label = Expect(CypherTokenType.Ident).Text;
            var condition = new CypherCondition(variable, null, "HAS_LABEL", label, negated, [], null);
            return new CypherExpr(CypherExprKind.Condition, Condition: condition);
        }

        string? property = null;
        if (Match(CypherTokenType.Dot))
        {
            property = Expect(CypherTokenType.Ident).Text;
        }

        if (Match(CypherTokenType.Is))
        {
            var notNull = Match(CypherTokenType.Not);
            Expect(CypherTokenType.NullKeyword);
            var op = notNull ? "IS NOT NULL" : "IS NULL";
            return new CypherExpr(
                CypherExprKind.Condition,
                Condition: new CypherCondition(variable, property, op, null, negated, [], null));
        }

        if (Peek().Type == CypherTokenType.In)
        {
            return ParseInList(variable, property, negated);
        }

        var comparison = ParseComparisonOperator();
        if (comparison is null)
        {
            throw new CypherParseException("unexpected operator", Peek().Position);
        }

        var value = ParseConditionValue();
        return new CypherExpr(
            CypherExprKind.Condition,
            Condition: new CypherCondition(variable, property, comparison, value, negated, [], null));
    }

    private CypherExpr ParseExistsPredicate(bool negated)
    {
        Advance();
        if (!Match(CypherTokenType.LeftBrace))
        {
            throw new CypherParseException("expected '{' after EXISTS", Peek().Position);
        }

        try
        {
            var anchor = ParseNode();
            var relationship = ParseRelationship();
            _ = ParseNode();

            if (relationship.MinHops != 1 || relationship.MaxHops is not (0 or 1))
            {
                throw Unsupported(
                    "unsupported EXISTS pattern — only the single-hop form '(var)-[:TYPE]->()' is supported");
            }

            var edgeType = relationship.Types.Count > 0 ? relationship.Types[0] : null;
            var existsDirection = relationship.Direction switch
            {
                CypherRelDirection.Inbound => CypherExistsDirection.Inbound,
                CypherRelDirection.Any => CypherExistsDirection.Any,
                _ => CypherExistsDirection.Outbound,
            };

            Expect(CypherTokenType.RightBrace);

            var condition = new CypherCondition(
                anchor.Variable ?? string.Empty,
                null,
                "EXISTS",
                edgeType,
                negated,
                [],
                existsDirection);

            return new CypherExpr(CypherExprKind.Condition, Condition: condition);
        }
        catch (CypherParseException)
        {
            throw new CypherParseException(
                "unsupported EXISTS pattern — only the single-hop form '(var)-[:TYPE]->()' is supported");
        }
    }

    private CypherExpr ParseInList(string variable, string? property, bool negated)
    {
        Advance();
        Expect(CypherTokenType.LeftBracket);

        var values = new List<string>();
        while (Peek().Type != CypherTokenType.RightBracket && Peek().Type != CypherTokenType.Eof)
        {
            if (values.Count > 0)
            {
                Match(CypherTokenType.Comma);
            }

            if (Peek().Type is CypherTokenType.String or CypherTokenType.Number)
            {
                values.Add(Advance().Text);
            }
            else
            {
                break;
            }
        }

        Expect(CypherTokenType.RightBracket);
        return new CypherExpr(
            CypherExprKind.Condition,
            Condition: new CypherCondition(variable, property, "IN", null, negated, values, null));
    }

    private string? ParseComparisonOperator()
    {
        if (Match(CypherTokenType.Equals))
        {
            return "=";
        }

        if (Match(CypherTokenType.Neq))
        {
            return "<>";
        }

        if (Match(CypherTokenType.EqTilde))
        {
            return "=~";
        }

        if (Match(CypherTokenType.GreaterThanOrEqual))
        {
            return ">=";
        }

        if (Match(CypherTokenType.LessThanOrEqual))
        {
            return "<=";
        }

        if (Match(CypherTokenType.GreaterThan))
        {
            return ">";
        }

        if (Match(CypherTokenType.LessThan))
        {
            return "<";
        }

        if (Match(CypherTokenType.Contains))
        {
            return "CONTAINS";
        }

        if (Match(CypherTokenType.Starts))
        {
            Expect(CypherTokenType.With);
            return "STARTS WITH";
        }

        if (Match(CypherTokenType.Ends))
        {
            Expect(CypherTokenType.With);
            return "ENDS WITH";
        }

        return null;
    }

    private string ParseConditionValue()
    {
        var token = Peek();
        switch (token.Type)
        {
            case CypherTokenType.String:
            case CypherTokenType.Number:
                Advance();
                return token.Text;
            case CypherTokenType.True:
                Advance();
                return "true";
            case CypherTokenType.False:
                Advance();
                return "false";
            default:
                throw new CypherParseException("expected value", token.Position);
        }
    }

    private int ParseIntegerLiteral(string clauseName)
    {
        if (Peek().Type != CypherTokenType.Number)
        {
            throw new CypherParseException($"expected number after {clauseName}", Peek().Position);
        }

        return int.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private CypherToken Peek() => tokens[Math.Min(position, tokens.Count - 1)];

    private CypherToken? PeekAhead(int offset)
    {
        var index = position + offset;
        return index < tokens.Count ? tokens[index] : null;
    }

    private CypherToken Advance() => tokens[position++];

    private bool Match(CypherTokenType type)
    {
        if (Peek().Type != type)
        {
            return false;
        }

        Advance();
        return true;
    }

    private CypherToken Expect(CypherTokenType type)
    {
        var token = Peek();
        if (token.Type != type)
        {
            throw new CypherParseException($"expected {type}, got {token.Type}", token.Position);
        }

        return Advance();
    }

    private static bool IsAggregateToken(CypherTokenType type) =>
        type is CypherTokenType.Count or CypherTokenType.Sum or CypherTokenType.Avg
            or CypherTokenType.MinKeyword or CypherTokenType.MaxKeyword or CypherTokenType.Collect;

    private static bool IsStringFunctionToken(CypherTokenType type) =>
        type is CypherTokenType.ToLower or CypherTokenType.ToUpper or CypherTokenType.ToString;

    private static string AggregateFunctionName(CypherTokenType type) => type switch
    {
        CypherTokenType.Count => "COUNT",
        CypherTokenType.Sum => "SUM",
        CypherTokenType.Avg => "AVG",
        CypherTokenType.MinKeyword => "MIN",
        CypherTokenType.MaxKeyword => "MAX",
        CypherTokenType.Collect => "COLLECT",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string StringFunctionName(CypherTokenType type) => type switch
    {
        CypherTokenType.ToLower => "toLower",
        CypherTokenType.ToUpper => "toUpper",
        CypherTokenType.ToString => "toString",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static void EnsureNotUnsupportedClause(CypherTokenType type, string context)
    {
        var message = UnsupportedClauseMessage(type);
        if (message is not null)
        {
            throw new CypherParseException(message);
        }
    }

    private static string? UnsupportedClauseMessage(CypherTokenType type) => type switch
    {
        CypherTokenType.Create => "unsupported Cypher feature: CREATE clause (write operations not supported)",
        CypherTokenType.Delete => "unsupported Cypher feature: DELETE clause (write operations not supported)",
        CypherTokenType.Detach => "unsupported Cypher feature: DETACH DELETE (write operations not supported)",
        CypherTokenType.Set => "unsupported Cypher feature: SET clause (write operations not supported)",
        CypherTokenType.Remove => "unsupported Cypher feature: REMOVE clause (write operations not supported)",
        CypherTokenType.Merge => "unsupported Cypher feature: MERGE clause (write operations not supported)",
        CypherTokenType.Yield => "unsupported Cypher feature: YIELD clause",
        CypherTokenType.Call => "unsupported Cypher feature: CALL clause (stored procedures not supported)",
        CypherTokenType.Foreach => "unsupported Cypher feature: FOREACH clause",
        CypherTokenType.Mandatory => "unsupported Cypher feature: MANDATORY MATCH",
        CypherTokenType.Drop => "unsupported Cypher feature: DROP (schema operations not supported)",
        CypherTokenType.Constraint => "unsupported Cypher feature: CONSTRAINT (schema operations not supported)",
        _ => null,
    };

    private static CypherParseException Unsupported(string message) => new(message);
}
