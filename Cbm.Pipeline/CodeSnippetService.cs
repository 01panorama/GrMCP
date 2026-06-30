using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class CodeSnippetService
{
    public CbmCodeSnippetResult GetSnippet(string projectName, string qualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedName);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            return NotFound(null, "project not found or not indexed");
        }

        using var store = CbmStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        if (project is null)
        {
            return NotFound(null, "project not indexed");
        }

        var exact = store.FindNodeByQualifiedName(projectName, qualifiedName);
        if (exact is not null)
        {
            return BuildSnippet(project.RootPath, exact, "exact");
        }

        var suffixMatches = store.FindNodesByQualifiedNameSuffix(projectName, qualifiedName);
        if (suffixMatches.Count == 1)
        {
            return BuildSnippet(project.RootPath, suffixMatches[0], "suffix");
        }

        if (suffixMatches.Count > 1)
        {
            var suggestions = suffixMatches
                .Select(node => node.QualifiedName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return new CbmCodeSnippetResult(
                Found: false,
                QualifiedName: qualifiedName,
                FilePath: null,
                StartLine: 0,
                EndLine: 0,
                Code: null,
                MatchType: "ambiguous",
                Suggestions: suggestions,
                Error: "multiple symbols match; pass an exact qualified_name");
        }

        return NotFound(qualifiedName, "symbol not found");
    }

    private static CbmCodeSnippetResult BuildSnippet(string repositoryRoot, CbmNode node, string matchType)
    {
        if (string.IsNullOrWhiteSpace(node.FilePath))
        {
            return NotFound(node.QualifiedName, "node has no file_path");
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot, node.FilePath));
        var normalizedRoot = Path.GetFullPath(repositoryRoot);
        if (!absolutePath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(absolutePath, normalizedRoot, StringComparison.Ordinal))
        {
            return NotFound(node.QualifiedName, "resolved file path is outside repository root");
        }

        if (!File.Exists(absolutePath))
        {
            return NotFound(node.QualifiedName, "source file not found on disk");
        }

        var lines = File.ReadAllLines(absolutePath);
        var startLine = Math.Max(1, node.StartLine);
        var endLine = node.EndLine > 0 ? node.EndLine : startLine;
        if (startLine > lines.Length)
        {
            return NotFound(node.QualifiedName, "node start_line is outside file bounds");
        }

        endLine = Math.Min(endLine, lines.Length);
        var snippet = string.Join(Environment.NewLine, lines.Skip(startLine - 1).Take(endLine - startLine + 1));
        return new CbmCodeSnippetResult(
            Found: true,
            QualifiedName: node.QualifiedName,
            FilePath: node.FilePath,
            StartLine: startLine,
            EndLine: endLine,
            Code: snippet,
            MatchType: matchType,
            Suggestions: null,
            Error: null);
    }

    private static CbmCodeSnippetResult NotFound(string? qualifiedName, string error)
    {
        return new CbmCodeSnippetResult(
            Found: false,
            QualifiedName: qualifiedName,
            FilePath: null,
            StartLine: 0,
            EndLine: 0,
            Code: null,
            MatchType: null,
            Suggestions: null,
            Error: error);
    }
}
