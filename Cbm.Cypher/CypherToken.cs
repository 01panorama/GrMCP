namespace Cbm.Cypher;

public sealed record CypherToken(CypherTokenType Type, string Text, int Position);
