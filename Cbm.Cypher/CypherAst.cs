namespace Cbm.Cypher;

public sealed record CypherPropFilter(string Key, string Value);

public sealed record CypherNodePattern(
    string? Variable,
    string? Label,
    IReadOnlyList<CypherPropFilter> Properties);

public enum CypherRelDirection
{
    Outbound,
    Inbound,
    Any,
}

public sealed record CypherRelPattern(
    string? Variable,
    IReadOnlyList<string> Types,
    CypherRelDirection Direction,
    int MinHops,
    int MaxHops);

public sealed record CypherPattern(
    IReadOnlyList<CypherNodePattern> Nodes,
    IReadOnlyList<CypherRelPattern> Relationships);

public enum CypherExistsDirection
{
    Outbound,
    Inbound,
    Any,
}

public sealed record CypherCondition(
    string Variable,
    string? Property,
    string Operator,
    string? Value,
    bool Negated,
    IReadOnlyList<string> InValues,
    CypherExistsDirection? ExistsDirection);

public enum CypherExprKind
{
    Condition,
    And,
    Or,
    Not,
    Xor,
}

public sealed record CypherExpr(
    CypherExprKind Kind,
    CypherCondition? Condition = null,
    CypherExpr? Left = null,
    CypherExpr? Right = null);

public sealed record CypherWhereClause(CypherExpr? Root);

public sealed record CypherReturnItem(
    string? Variable,
    string? Property,
    string? Alias,
    string? Function,
    bool Distinct,
    IReadOnlyList<CypherFuncArg> Arguments);

public sealed record CypherFuncArg(
    string? Variable,
    string? Property,
    string? Literal);

public sealed record CypherReturnClause(
    IReadOnlyList<CypherReturnItem> Items,
    bool Distinct,
    bool Star,
    string? OrderBy,
    string? OrderDirection,
    int Skip,
    int Limit);

public sealed record CypherQuery(
    IReadOnlyList<CypherPattern> Patterns,
    IReadOnlyList<bool> PatternOptional,
    CypherWhereClause? Where,
    CypherReturnClause? Return,
    CypherQuery? UnionNext,
    bool UnionAll);
