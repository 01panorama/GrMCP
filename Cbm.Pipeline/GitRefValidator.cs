namespace Cbm.Pipeline;

public static class GitRefValidator
{
    public static bool IsValidRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\'':
                case '"':
                case ';':
                case '|':
                case '&':
                case '$':
                case '`':
                case '<':
                case '>':
                case '\n':
                case '\r':
                    return false;
                case '\\' when !OperatingSystem.IsWindows():
                    return false;
            }
        }

        return true;
    }

    public static bool IsValidRepoPath(string? value)
    {
        return IsValidRef(value);
    }
}
