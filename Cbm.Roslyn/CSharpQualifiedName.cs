using Microsoft.CodeAnalysis;

namespace Cbm.Roslyn;

public static class CSharpQualifiedName
{
    private static readonly SymbolDisplayFormat SymbolFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string ForFile(string project, string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? project : $"{project}.{string.Join('.', segments)}";
    }

    public static string ForSymbol(string project, ISymbol symbol)
    {
        string display = symbol.ToDisplayString(SymbolFormat);
        return string.IsNullOrWhiteSpace(display) ? project : $"{project}.{display}";
    }

    public static bool IsWithinRepository(string repoRoot, string filePath)
    {
        string root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(filePath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToRelativePath(string repoRoot, string filePath)
    {
        string root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(filePath);
        if (IsWithinRepository(repoRoot, filePath))
        {
            return full[(root.Length + 1)..].Replace('\\', '/');
        }

        return Path.GetFileName(full);
    }
}
