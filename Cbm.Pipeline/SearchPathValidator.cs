namespace Cbm.Pipeline;

public static class SearchPathValidator
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
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

        // Parity with CBM validate_search_path_arg; search_code uses .NET I/O only, not shell strings.
        return true;
    }
}
