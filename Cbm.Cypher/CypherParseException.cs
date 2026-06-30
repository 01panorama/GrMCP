namespace Cbm.Cypher;

public sealed class CypherParseException : Exception
{
    public CypherParseException(string message)
        : base(message)
    {
    }

    public CypherParseException(string message, int position)
        : base($"{message} at position {position}")
    {
        Position = position;
    }

    public int? Position { get; }
}
