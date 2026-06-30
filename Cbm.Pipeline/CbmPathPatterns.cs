using System.Text;
using System.Text.RegularExpressions;

namespace Cbm.Pipeline;

internal static class CbmPathPatterns
{
    public static bool MatchesGlob(string relativePath, string globPattern)
    {
        if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(globPattern))
        {
            return false;
        }

        var normalizedPath = relativePath.Replace('\\', '/');
        var regex = new Regex("^" + GlobToRegex(globPattern) + "$", RegexOptions.CultureInvariant);
        return regex.IsMatch(normalizedPath);
    }

    private static string GlobToRegex(string pattern)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == '*')
            {
                var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                builder.Append(isDoubleStar ? ".*" : "[^/]*");
                if (isDoubleStar)
                {
                    i++;
                }
            }
            else if (current == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(current.ToString()));
            }
        }

        return builder.ToString();
    }
}
