using System.Text;

namespace Cbm.Cypher;

internal static class CypherLexer
{
    private static readonly Dictionary<string, CypherTokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MATCH"] = CypherTokenType.Match,
        ["WHERE"] = CypherTokenType.Where,
        ["RETURN"] = CypherTokenType.Return,
        ["ORDER"] = CypherTokenType.Order,
        ["BY"] = CypherTokenType.By,
        ["LIMIT"] = CypherTokenType.Limit,
        ["AND"] = CypherTokenType.And,
        ["OR"] = CypherTokenType.Or,
        ["AS"] = CypherTokenType.As,
        ["DISTINCT"] = CypherTokenType.Distinct,
        ["COUNT"] = CypherTokenType.Count,
        ["CONTAINS"] = CypherTokenType.Contains,
        ["STARTS"] = CypherTokenType.Starts,
        ["WITH"] = CypherTokenType.With,
        ["NOT"] = CypherTokenType.Not,
        ["ASC"] = CypherTokenType.Asc,
        ["DESC"] = CypherTokenType.Desc,
        ["ENDS"] = CypherTokenType.Ends,
        ["IN"] = CypherTokenType.In,
        ["IS"] = CypherTokenType.Is,
        ["NULL"] = CypherTokenType.NullKeyword,
        ["XOR"] = CypherTokenType.Xor,
        ["SKIP"] = CypherTokenType.Skip,
        ["UNION"] = CypherTokenType.Union,
        ["UNWIND"] = CypherTokenType.Unwind,
        ["SUM"] = CypherTokenType.Sum,
        ["AVG"] = CypherTokenType.Avg,
        ["MIN"] = CypherTokenType.MinKeyword,
        ["MAX"] = CypherTokenType.MaxKeyword,
        ["COLLECT"] = CypherTokenType.Collect,
        ["toLower"] = CypherTokenType.ToLower,
        ["toUpper"] = CypherTokenType.ToUpper,
        ["toString"] = CypherTokenType.ToString,
        ["tolower"] = CypherTokenType.ToLower,
        ["toupper"] = CypherTokenType.ToUpper,
        ["tostring"] = CypherTokenType.ToString,
        ["CASE"] = CypherTokenType.Case,
        ["WHEN"] = CypherTokenType.When,
        ["THEN"] = CypherTokenType.Then,
        ["ELSE"] = CypherTokenType.Else,
        ["END"] = CypherTokenType.End,
        ["OPTIONAL"] = CypherTokenType.Optional,
        ["CREATE"] = CypherTokenType.Create,
        ["DELETE"] = CypherTokenType.Delete,
        ["DETACH"] = CypherTokenType.Detach,
        ["SET"] = CypherTokenType.Set,
        ["REMOVE"] = CypherTokenType.Remove,
        ["MERGE"] = CypherTokenType.Merge,
        ["YIELD"] = CypherTokenType.Yield,
        ["CALL"] = CypherTokenType.Call,
        ["ALL"] = CypherTokenType.All,
        ["TRUE"] = CypherTokenType.True,
        ["FALSE"] = CypherTokenType.False,
        ["EXISTS"] = CypherTokenType.Exists,
        ["MANDATORY"] = CypherTokenType.Mandatory,
        ["FOREACH"] = CypherTokenType.Foreach,
        ["ON"] = CypherTokenType.On,
        ["ADD"] = CypherTokenType.Add,
        ["CONSTRAINT"] = CypherTokenType.Constraint,
        ["DO"] = CypherTokenType.Do,
        ["DROP"] = CypherTokenType.Drop,
        ["FOR"] = CypherTokenType.For,
        ["FROM"] = CypherTokenType.From,
        ["GRAPH"] = CypherTokenType.Graph,
        ["OF"] = CypherTokenType.Of,
        ["REQUIRE"] = CypherTokenType.Require,
        ["SCALAR"] = CypherTokenType.Scalar,
        ["UNIQUE"] = CypherTokenType.Unique,
    };

    public static IReadOnlyList<CypherToken> Lex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokens = new List<CypherToken>();
        var index = 0;

        while (index < input.Length)
        {
            if (TrySkipWhitespaceOrComment(input, ref index))
            {
                continue;
            }

            var position = index;
            var current = input[index];

            if (current is '"' or '\'')
            {
                tokens.Add(ReadStringLiteral(input, ref index, current, position));
                continue;
            }

            if (TryReadNumber(input, ref index, out var numberText))
            {
                tokens.Add(new CypherToken(CypherTokenType.Number, numberText, position));
                continue;
            }

            if (TryReadIdentifier(input, ref index, out var identifier))
            {
                var type = Keywords.TryGetValue(identifier, out var keyword)
                    ? keyword
                    : CypherTokenType.Ident;
                tokens.Add(new CypherToken(type, identifier, position));
                continue;
            }

            if (TryReadTwoCharToken(input, ref index, out var twoCharType, out var twoCharText))
            {
                tokens.Add(new CypherToken(twoCharType, twoCharText, position));
                continue;
            }

            if (TryReadSingleCharToken(current, out var singleCharType))
            {
                tokens.Add(new CypherToken(singleCharType, current.ToString(), position));
                index++;
                continue;
            }

            index++;
        }

        tokens.Add(new CypherToken(CypherTokenType.Eof, string.Empty, input.Length));
        return tokens;
    }

    private static bool TrySkipWhitespaceOrComment(string input, ref int index)
    {
        if (char.IsWhiteSpace(input[index]))
        {
            index++;
            return true;
        }

        if (index + 1 < input.Length && input[index] == '/' && input[index + 1] == '/')
        {
            index += 2;
            while (index < input.Length && input[index] != '\n')
            {
                index++;
            }

            return true;
        }

        if (index + 1 < input.Length && input[index] == '-' && input[index + 1] == '-')
        {
            index += 2;
            while (index < input.Length && input[index] != '\n')
            {
                index++;
            }

            return true;
        }

        if (index + 1 < input.Length && input[index] == '/' && input[index + 1] == '*')
        {
            index += 2;
            while (index + 1 < input.Length && !(input[index] == '*' && input[index + 1] == '/'))
            {
                index++;
            }

            if (index + 1 < input.Length)
            {
                index += 2;
            }

            return true;
        }

        return false;
    }

    private static CypherToken ReadStringLiteral(string input, ref int index, char quote, int position)
    {
        index++;
        var builder = new StringBuilder();

        while (index < input.Length && input[index] != quote)
        {
            if (input[index] == '\\' && index + 1 < input.Length)
            {
                index++;
                builder.Append(input[index] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    _ => input[index],
                });
            }
            else
            {
                builder.Append(input[index]);
            }

            index++;
        }

        if (index < input.Length)
        {
            index++;
        }

        return new CypherToken(CypherTokenType.String, builder.ToString(), position);
    }

    private static bool TryReadNumber(string input, ref int index, out string numberText)
    {
        numberText = string.Empty;
        var start = index;
        var current = input[index];

        if (!char.IsDigit(current) &&
            !(current == '.' && index + 1 < input.Length && char.IsDigit(input[index + 1])))
        {
            return false;
        }

        while (index < input.Length)
        {
            current = input[index];
            if (char.IsDigit(current))
            {
                index++;
                continue;
            }

            if (current == '.' && index + 1 < input.Length && input[index + 1] != '.')
            {
                index++;
                continue;
            }

            break;
        }

        numberText = input[start..index];
        return true;
    }

    private static bool TryReadIdentifier(string input, ref int index, out string identifier)
    {
        identifier = string.Empty;
        var current = input[index];

        if (!char.IsLetter(current) && current != '_')
        {
            return false;
        }

        var start = index;
        index++;
        while (index < input.Length)
        {
            current = input[index];
            if (!char.IsLetterOrDigit(current) && current != '_')
            {
                break;
            }

            index++;
        }

        identifier = input[start..index];
        return true;
    }

    private static bool TryReadTwoCharToken(
        string input,
        ref int index,
        out CypherTokenType type,
        out string text)
    {
        type = CypherTokenType.Eof;
        text = string.Empty;

        if (index + 1 >= input.Length)
        {
            return false;
        }

        var pair = input.AsSpan(index, 2);
        switch (pair)
        {
            case "!=":
                type = CypherTokenType.Neq;
                text = "!=";
                break;
            case "<>":
                type = CypherTokenType.Neq;
                text = "<>";
                break;
            case "=~":
                type = CypherTokenType.EqTilde;
                text = "=~";
                break;
            case ">=":
                type = CypherTokenType.GreaterThanOrEqual;
                text = ">=";
                break;
            case "<=":
                type = CypherTokenType.LessThanOrEqual;
                text = "<=";
                break;
            case "..":
                type = CypherTokenType.DotDot;
                text = "..";
                break;
            default:
                return false;
        }

        index += 2;
        return true;
    }

    private static bool TryReadSingleCharToken(char current, out CypherTokenType type)
    {
        switch (current)
        {
            case '(':
                type = CypherTokenType.LeftParen;
                return true;
            case ')':
                type = CypherTokenType.RightParen;
                return true;
            case '[':
                type = CypherTokenType.LeftBracket;
                return true;
            case ']':
                type = CypherTokenType.RightBracket;
                return true;
            case '-':
                type = CypherTokenType.Dash;
                return true;
            case '>':
                type = CypherTokenType.GreaterThan;
                return true;
            case '<':
                type = CypherTokenType.LessThan;
                return true;
            case ':':
                type = CypherTokenType.Colon;
                return true;
            case '.':
                type = CypherTokenType.Dot;
                return true;
            case '{':
                type = CypherTokenType.LeftBrace;
                return true;
            case '}':
                type = CypherTokenType.RightBrace;
                return true;
            case '*':
                type = CypherTokenType.Star;
                return true;
            case ',':
                type = CypherTokenType.Comma;
                return true;
            case '=':
                type = CypherTokenType.Equals;
                return true;
            case '|':
                type = CypherTokenType.Pipe;
                return true;
            default:
                type = CypherTokenType.Eof;
                return false;
        }
    }
}
