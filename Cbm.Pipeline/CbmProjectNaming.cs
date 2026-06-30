namespace Cbm.Pipeline;

public static class CbmProjectNaming
{
    public static string DeriveFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        if (normalized.Length == 0)
        {
            return "root";
        }

        Span<char> buffer = stackalloc char[normalized.Length];
        var length = 0;
        char previous = '\0';
        for (var i = 0; i < normalized.Length; i++)
        {
            var current = NormalizeCharacter(normalized[i]);
            if ((current == '-' && previous == '-') || (current == '.' && previous == '.'))
            {
                continue;
            }

            buffer[length++] = current;
            previous = current;
        }

        var start = 0;
        while (start < length && (buffer[start] == '-' || buffer[start] == '.'))
        {
            start++;
        }

        while (length > start && buffer[length - 1] == '-')
        {
            length--;
        }

        if (start >= length)
        {
            return "root";
        }

        return new string(buffer.Slice(start, length - start));
    }

    public static bool IsValidProjectName(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        if (projectName[0] == '.')
        {
            return false;
        }

        if (projectName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in projectName)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static char NormalizeCharacter(char value)
    {
        return IsAllowedCharacter(value) ? value : '-';
    }

    private static bool IsAllowedCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';
    }
}
