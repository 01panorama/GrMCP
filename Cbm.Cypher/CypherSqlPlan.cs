namespace Cbm.Cypher;

public sealed record CypherSqlParameter(string Name, object? Value);

public sealed record CypherSqlPlan(string Sql, IReadOnlyList<CypherSqlParameter> Parameters);

public sealed class CypherPlanException : Exception
{
    public CypherPlanException(string message)
        : base(message)
    {
    }
}

public sealed class CypherExecuteException : Exception
{
    public CypherExecuteException(string message)
        : base(message)
    {
    }

    public CypherExecuteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
