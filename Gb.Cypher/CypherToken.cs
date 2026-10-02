namespace Gb.Cypher;

public sealed record CypherToken(CypherTokenType Type, string Text, int Position);
