using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class SearchCodeService
{
    private const int GrepMaxMatches = 500;
    private const int MaxRawOutput = 20;
    private const long SearchSlowMs = 5000;
    private const int ScoreMethod = 10;
    private const int ScoreVendored = -50;
    private const int ScoreTest = -5;
    private const int MaxMatchLinesPerResult = 64;

    public CbmSearchCodeResult Search(
        string projectName,
        string pattern,
        string? filePattern = null,
        string? pathFilter = null,
        string? mode = null,
        int contextLines = 0,
        bool useRegex = false,
        int limit = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var normalizedLimit = limit <= 0 ? 10 : limit;
        var searchMode = ParseSearchMode(mode);
        var patHasPipe = pattern.Contains('|', StringComparison.Ordinal);

        using var store = OpenProjectStore(projectName);
        var project = store.GetProject(projectName)
            ?? throw new InvalidOperationException("project not indexed");

        if (!ValidateSearchPathArg(project.RootPath)
            || (!string.IsNullOrWhiteSpace(filePattern) && !ValidateSearchPathArg(filePattern)))
        {
            throw new ArgumentException("path or file_pattern contains invalid characters");
        }

        Regex? pathRegex = null;
        if (!string.IsNullOrWhiteSpace(pathFilter))
        {
            try
            {
                pathRegex = new Regex(pathFilter, RegexOptions.Compiled);
            }
            catch (ArgumentException)
            {
                // CBM silently ignores invalid path_filter.
            }
        }

        var effectiveUseRegex = useRegex;
        var effectivePattern = pattern;
        if (!useRegex && pattern.AsSpan().ContainsAny(' ', '\t'))
        {
            effectivePattern = ConvertMultiWordToRegex(pattern);
            effectiveUseRegex = true;
        }

        Regex? matchRegex = null;
        if (effectiveUseRegex)
        {
            try
            {
                matchRegex = new Regex(effectivePattern, RegexOptions.Compiled);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(
                    "invalid regex pattern (regex=true): check for unbalanced (), [], or {}");
            }
        }

        if (patHasPipe && !useRegex)
        {
            warnings.Add(
                "pattern contains '|' but regex=false, so it is matched literally (not as "
                + "alternation). Pass regex=true for 'foo|bar' to mean 'foo OR bar'.");
        }

        var rootPath = Path.GetFullPath(project.RootPath);
        var indexedFiles = store.ListFiles(projectName);
        var grepMatches = ScanFiles(
            rootPath,
            indexedFiles,
            effectivePattern,
            effectiveUseRegex,
            matchRegex,
            filePattern,
            pathRegex);

        var rawMatches = new List<CbmGrepMatch>();
        var resultMap = new Dictionary<long, MutableSearchHit>();

        foreach (var fileGroup in grepMatches.GroupBy(match => match.File, StringComparer.Ordinal))
        {
            var fileNodes = store.FindNodesByFile(projectName, fileGroup.Key);
            foreach (var match in fileGroup)
            {
                var bestNode = FindTightestNode(fileNodes, match.Line);
                if (bestNode is not null)
                {
                    AddToSearchResults(resultMap, bestNode, match.Line);
                }
                else
                {
                    rawMatches.Add(match);
                }
            }
        }

        var degrees = store.BatchCountDegrees(resultMap.Keys.ToArray(), "CALLS");
        var rankedHits = resultMap.Values
            .Select(hit =>
            {
                degrees.TryGetValue(hit.Node.Id, out var degree);
                var inDegree = degree?.InDegree ?? 0;
                var outDegree = degree?.OutDegree ?? 0;
                var score = ComputeSearchScore(hit.Node, inDegree);
                return new RankedSearchHit(hit.Node, hit.MatchLines, inDegree, outDegree, score);
            })
            .OrderByDescending(hit => hit.Score)
            .ToList();

        var outputHits = rankedHits.Take(normalizedLimit).ToList();
        IReadOnlyList<string>? files = null;
        List<CbmSearchCodeHit> enrichedResults;

        if (searchMode == SearchCodeMode.Files)
        {
            enrichedResults = [];
            files = BuildDedupFiles(outputHits, rawMatches);
        }
        else
        {
            enrichedResults = BuildEnrichedResults(
                rootPath,
                outputHits,
                searchMode,
                contextLines);
        }

        var elapsedMs = stopwatch.ElapsedMilliseconds;
        if (elapsedMs >= SearchSlowMs)
        {
            warnings.Add(
                $"search took {elapsedMs}ms (>{SearchSlowMs / 1000}s); narrow file_pattern/path_filter "
                + "or use a more specific pattern");
        }

        string? dedupRatio = null;
        var totalDeduped = rankedHits.Count + rawMatches.Count;
        if (totalDeduped > 0 && grepMatches.Count > 0)
        {
            dedupRatio = $"{(double)grepMatches.Count / totalDeduped:F1}x";
        }

        return new CbmSearchCodeResult(
            enrichedResults,
            rawMatches.Take(MaxRawOutput).ToArray(),
            files,
            BuildDirectoryDistribution(rankedHits.Select(hit => hit.Node.FilePath)),
            grepMatches.Count,
            rankedHits.Count,
            rawMatches.Count,
            elapsedMs,
            dedupRatio,
            warnings);
    }

    private static List<CbmSearchCodeHit> BuildEnrichedResults(
        string rootPath,
        IReadOnlyList<RankedSearchHit> outputHits,
        SearchCodeMode searchMode,
        int contextLines)
    {
        var enrichedResults = new List<CbmSearchCodeHit>(outputHits.Count);
        foreach (var hit in outputHits)
        {
            string? source = null;
            string? context = null;
            int? contextStart = null;

            if (searchMode == SearchCodeMode.Full)
            {
                source = ReadFileLines(rootPath, hit.Node.FilePath, hit.Node.StartLine, hit.Node.EndLine);
            }
            else if (searchMode == SearchCodeMode.Compact && contextLines > 0 && hit.MatchLines.Count > 0)
            {
                var first = hit.MatchLines.Min();
                var last = hit.MatchLines.Max();
                var ctxStart = Math.Max(1, first - contextLines);
                var ctxEnd = last + contextLines;
                context = ReadFileLines(rootPath, hit.Node.FilePath, ctxStart, ctxEnd);
                contextStart = ctxStart;
            }

            enrichedResults.Add(new CbmSearchCodeHit(
                hit.Node.Id,
                hit.Node.Name,
                hit.Node.QualifiedName,
                hit.Node.Label,
                hit.Node.FilePath,
                hit.Node.StartLine,
                hit.Node.EndLine,
                hit.InDegree,
                hit.OutDegree,
                hit.Score,
                hit.MatchLines.OrderBy(line => line).ToArray(),
                source,
                context,
                contextStart));
        }

        return enrichedResults;
    }

    private static List<CbmGrepMatch> ScanFiles(
        string rootPath,
        IReadOnlyList<string> indexedFiles,
        string pattern,
        bool useRegex,
        Regex? matchRegex,
        string? filePattern,
        Regex? pathRegex)
    {
        var matches = new List<CbmGrepMatch>();
        foreach (var relativePath in indexedFiles)
        {
            if (matches.Count >= GrepMaxMatches)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(filePattern)
                && !CbmPathPatterns.MatchesGlob(relativePath, filePattern))
            {
                continue;
            }

            if (pathRegex is not null && !pathRegex.IsMatch(relativePath))
            {
                continue;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            if (!IsPathUnderRoot(absolutePath, rootPath) || !File.Exists(absolutePath))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(absolutePath);
            }
            catch (IOException)
            {
                continue;
            }

            for (var lineIndex = 0; lineIndex < lines.Length && matches.Count < GrepMaxMatches; lineIndex++)
            {
                var line = lines[lineIndex];
                if (!LineMatches(line, pattern, useRegex, matchRegex))
                {
                    continue;
                }

                matches.Add(new CbmGrepMatch(
                    relativePath,
                    lineIndex + 1,
                    SanitizeAscii(line)));
            }
        }

        return matches;
    }

    private static bool LineMatches(string line, string pattern, bool useRegex, Regex? matchRegex)
    {
        if (useRegex)
        {
            return matchRegex!.IsMatch(line);
        }

        return line.Contains(pattern, StringComparison.Ordinal);
    }

    private static CbmNode? FindTightestNode(IReadOnlyList<CbmNode> nodes, int line)
    {
        CbmNode? best = null;
        var bestSpan = int.MaxValue;
        foreach (var node in nodes)
        {
            if (node.StartLine > line || node.EndLine < line || node.EndLine < node.StartLine)
            {
                continue;
            }

            var span = node.EndLine - node.StartLine;
            if (span < bestSpan)
            {
                bestSpan = span;
                best = node;
            }
        }

        return best;
    }

    private static void AddToSearchResults(
        Dictionary<long, MutableSearchHit> results,
        CbmNode node,
        int line)
    {
        if (results.TryGetValue(node.Id, out var existing))
        {
            if (existing.MatchLines.Count < MaxMatchLinesPerResult)
            {
                existing.MatchLines.Add(line);
            }

            return;
        }

        results[node.Id] = new MutableSearchHit(node, [line]);
    }

    private static int ComputeSearchScore(CbmNode node, int inDegree)
    {
        var score = inDegree;
        if (string.Equals(node.Label, "Method", StringComparison.Ordinal)
            || string.Equals(node.Label, "Constructor", StringComparison.Ordinal))
        {
            score += ScoreMethod;
        }

        var file = node.FilePath;
        if (file.Contains("vendored/", StringComparison.Ordinal)
            || file.Contains("vendor/", StringComparison.Ordinal)
            || file.Contains("node_modules/", StringComparison.Ordinal))
        {
            score += ScoreVendored;
        }

        if (CbmTracePathResolver.IsTestFile(file))
        {
            score += ScoreTest;
        }

        return score;
    }

    private static IReadOnlyDictionary<string, int> BuildDirectoryDistribution(IEnumerable<string> files)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var slash = file.IndexOf('/', StringComparison.Ordinal);
            var top = slash >= 0 ? file[..(slash + 1)].TrimEnd('/') : file;
            if (string.IsNullOrEmpty(top))
            {
                top = file;
            }

            counts.TryGetValue(top, out var count);
            counts[top] = count + 1;
        }

        return counts;
    }

    private static IReadOnlyList<string> BuildDedupFiles(
        IReadOnlyList<RankedSearchHit> rankedHits,
        IReadOnlyList<CbmGrepMatch> rawMatches)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<string>();
        foreach (var hit in rankedHits)
        {
            if (seen.Add(hit.Node.FilePath))
            {
                files.Add(hit.Node.FilePath);
            }
        }

        foreach (var raw in rawMatches)
        {
            if (seen.Add(raw.File))
            {
                files.Add(raw.File);
            }
        }

        return files;
    }

    private static string? ReadFileLines(string rootPath, string relativePath, int startLine, int endLine)
    {
        if (startLine <= 0 || endLine <= 0)
        {
            return null;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!IsPathUnderRoot(absolutePath, rootPath) || !File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(absolutePath);
            var start = Math.Max(1, startLine);
            var end = Math.Min(endLine, lines.Length);
            if (start > lines.Length)
            {
                return null;
            }

            return SanitizeAscii(string.Join(Environment.NewLine, lines.Skip(start - 1).Take(end - start + 1)));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string SanitizeAscii(string value)
    {
        if (value.All(static ch => ch <= 127))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch <= 127 ? ch : '?');
        }

        return builder.ToString();
    }

    private static string ConvertMultiWordToRegex(string pattern)
    {
        var builder = new StringBuilder(pattern.Length * 2);
        var inSpace = false;
        foreach (var ch in pattern)
        {
            if (ch is ' ' or '\t')
            {
                if (!inSpace)
                {
                    builder.Append(".*");
                    inSpace = true;
                }

                continue;
            }

            if ("\\^$.|?*+()[]{}".Contains(ch))
            {
                builder.Append('\\');
            }

            builder.Append(ch);
            inSpace = false;
        }

        return builder.ToString();
    }

    private static bool ValidateSearchPathArg(string value)
    {
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
                case '\\':
                    return false;
            }
        }

        return true;
    }

    private static bool IsPathUnderRoot(string absolutePath, string rootPath)
    {
        return absolutePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(absolutePath, rootPath, StringComparison.Ordinal);
    }

    private static SearchCodeMode ParseSearchMode(string? mode)
    {
        if (string.Equals(mode, "full", StringComparison.Ordinal))
        {
            return SearchCodeMode.Full;
        }

        if (string.Equals(mode, "files", StringComparison.Ordinal))
        {
            return SearchCodeMode.Files;
        }

        return SearchCodeMode.Compact;
    }

    private static CbmStore OpenProjectStore(string projectName)
    {
        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Project database not found for '{projectName}'.", databasePath);
        }

        return CbmStore.OpenPath(databasePath);
    }

    private sealed class MutableSearchHit(CbmNode node, List<int> matchLines)
    {
        public CbmNode Node { get; } = node;
        public List<int> MatchLines { get; } = matchLines;
    }

    private sealed record RankedSearchHit(
        CbmNode Node,
        IReadOnlyList<int> MatchLines,
        int InDegree,
        int OutDegree,
        int Score);

    private enum SearchCodeMode
    {
        Compact,
        Full,
        Files,
    }
}
