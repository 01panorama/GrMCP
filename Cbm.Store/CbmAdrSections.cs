using System.Text;

namespace Cbm.Store;

public static class CbmAdrSections
{
    public const int MaxLength = 8000;

    private static readonly string[] CanonicalSections =
    [
        "PURPOSE",
        "STACK",
        "ARCHITECTURE",
        "PATTERNS",
        "TRADEOFFS",
        "PHILOSOPHY",
    ];

    public static IReadOnlyDictionary<string, string> ParseSections(string? content)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content))
        {
            return sections;
        }

        string? currentKey = null;
        var currentContent = new StringBuilder();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryParseCanonicalHeader(line, out var header))
            {
                SaveSection(sections, currentKey, currentContent);
                currentKey = header;
                currentContent.Clear();
                continue;
            }

            if (currentKey is null)
            {
                continue;
            }

            if (currentContent.Length > 0 || line.Length > 0)
            {
                if (currentContent.Length > 0)
                {
                    currentContent.Append('\n');
                }

                currentContent.Append(line);
            }
        }

        SaveSection(sections, currentKey, currentContent);
        return sections;
    }

    public static string RenderSections(IReadOnlyDictionary<string, string> sections)
    {
        if (sections.Count == 0)
        {
            return string.Empty;
        }

        var rendered = new List<string>();
        var renderedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var canonical in CanonicalSections)
        {
            if (!sections.TryGetValue(canonical, out var value))
            {
                continue;
            }

            rendered.Add(RenderSection(canonical, value));
            renderedKeys.Add(canonical);
        }

        foreach (var key in sections.Keys
                     .Where(key => !renderedKeys.Contains(key))
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            rendered.Add(RenderSection(key, sections[key]));
        }

        return string.Join("\n\n", rendered);
    }

    public static IReadOnlyList<string> ListSectionHeaders(string? content)
    {
        var headers = new List<string>();
        if (string.IsNullOrEmpty(content))
        {
            return headers;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length > 0 && line[0] == '#')
            {
                headers.Add(line);
            }
        }

        return headers;
    }

    private static bool TryParseCanonicalHeader(string line, out string header)
    {
        header = string.Empty;
        if (!line.StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = line[3..].TrimEnd(' ', '\t', '\r');
        if (!CanonicalSections.Contains(candidate, StringComparer.Ordinal))
        {
            return false;
        }

        header = candidate;
        return true;
    }

    private static void SaveSection(
        IDictionary<string, string> sections,
        string? key,
        StringBuilder content)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        sections[key] = content.ToString().Trim();
    }

    private static string RenderSection(string key, string value) => $"## {key}\n{value}";
}
