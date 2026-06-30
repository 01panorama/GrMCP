namespace Cbm.Store;

internal static class CbmArchitecturePath
{
    public const string ScopeSql = " AND (file_path = $scopePath OR file_path LIKE $scopeLike)";

    public static bool TryPrepare(string? path, out string normalized, out string likePattern)
    {
        normalized = string.Empty;
        likePattern = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith("./", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        while (trimmed.StartsWith('/'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        trimmed = trimmed.TrimEnd(' ', '\t', '/');
        if (trimmed.Length == 0)
        {
            return false;
        }

        var collapsed = new List<char>(trimmed.Length);
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '/' && collapsed.Count > 0 && collapsed[^1] == '/')
            {
                continue;
            }

            collapsed.Add(trimmed[i]);
        }

        normalized = new string(collapsed.ToArray());
        if (normalized.Length == 0)
        {
            return false;
        }

        likePattern = normalized + "/%";
        return true;
    }
}
