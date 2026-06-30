using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cbm.Roslyn;

public static class SymbolResolutionProbe
{
    private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    public static IReadOnlyList<DeclaredSymbolProbeResult> GetDeclaredSymbols(
        SemanticModel semanticModel,
        SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(root);

        var results = new List<DeclaredSymbolProbeResult>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var node in root.DescendantNodesAndSelf())
        {
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol is null || !seen.Add(symbol))
            {
                continue;
            }

            var location = node.GetLocation().GetLineSpan();
            results.Add(new DeclaredSymbolProbeResult(
                symbol.Name,
                symbol.Kind.ToString(),
                symbol.ToDisplayString(DisplayFormat),
                semanticModel.SyntaxTree.FilePath,
                location.StartLinePosition.Line + 1,
                location.EndLinePosition.Line + 1));
        }

        return results;
    }

    public static IReadOnlyList<InvocationTargetProbeResult> ResolveInvocationTargets(
        SemanticModel semanticModel,
        SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(root);

        var results = new List<InvocationTargetProbeResult>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (target is null)
            {
                continue;
            }

            var location = invocation.GetLocation().GetLineSpan();
            results.Add(new InvocationTargetProbeResult(
                invocation.Expression.ToString(),
                target.Name,
                target.Kind.ToString(),
                target.ToDisplayString(DisplayFormat),
                semanticModel.SyntaxTree.FilePath,
                location.StartLinePosition.Line + 1,
                location.EndLinePosition.Line + 1));
        }

        return results;
    }
}

public sealed record DeclaredSymbolProbeResult(
    string Name,
    string Kind,
    string DisplayName,
    string FilePath,
    int StartLine,
    int EndLine);

public sealed record InvocationTargetProbeResult(
    string Expression,
    string TargetName,
    string TargetKind,
    string TargetDisplayName,
    string FilePath,
    int StartLine,
    int EndLine);
