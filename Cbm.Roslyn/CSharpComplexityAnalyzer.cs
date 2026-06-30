using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cbm.Roslyn;

internal sealed class CSharpComplexityMetrics
{
    public int Complexity { get; init; }
    public int Cognitive { get; init; }
    public int LoopCount { get; init; }
    public int LoopDepth { get; init; }
    public int LinearScanInLoop { get; init; }
    public int AllocInLoop { get; init; }
    public bool SelfRecursive { get; init; }
    public bool RecursionInLoop { get; init; }
    public bool UnguardedRecursion { get; init; }
    public int ParamCount { get; init; }
    public int MaxAccessDepth { get; init; }
    public int Lines { get; init; }
}

internal static class CSharpComplexityAnalyzer
{
    private static readonly HashSet<string> LinearScanMethods = new(StringComparer.Ordinal)
    {
        "Contains",
        "IndexOf",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Find",
        "Any",
        "All",
    };

    private static readonly HashSet<string> AllocMethods = new(StringComparer.Ordinal)
    {
        "ToList",
        "ToArray",
        "Append",
        "Add",
    };

    public static CSharpComplexityMetrics Analyze(
        SemanticModel semanticModel,
        SyntaxNode executableNode,
        IMethodSymbol? methodSymbol)
    {
        var body = GetExecutableBody(executableNode);
        var walkRoot = body ?? (SyntaxNode?)GetExpressionBody(executableNode);
        var paramCount = methodSymbol?.Parameters.Length ?? 0;
        var lines = CountLines(walkRoot ?? executableNode);

        if (walkRoot is null)
        {
            return new CSharpComplexityMetrics
            {
                Complexity = 1,
                Cognitive = 0,
                ParamCount = paramCount,
                Lines = lines,
            };
        }

        var cyclomatic = 1;
        var cognitive = 0;
        var loopCount = 0;
        var maxLoopDepth = 0;
        var linearScanInLoop = 0;
        var allocInLoop = 0;
        var selfRecursive = false;
        var recursionInLoop = false;
        var unguardedRecursion = false;
        var maxAccessDepth = 0;

        Walk(
            walkRoot,
            semanticModel,
            methodSymbol,
            branchNesting: 0,
            loopNesting: 0,
            insideConditional: false,
            ref cyclomatic,
            ref cognitive,
            ref loopCount,
            ref maxLoopDepth,
            ref linearScanInLoop,
            ref allocInLoop,
            ref selfRecursive,
            ref recursionInLoop,
            ref unguardedRecursion,
            ref maxAccessDepth);

        return new CSharpComplexityMetrics
        {
            Complexity = cyclomatic,
            Cognitive = cognitive,
            LoopCount = loopCount,
            LoopDepth = maxLoopDepth,
            LinearScanInLoop = linearScanInLoop,
            AllocInLoop = allocInLoop,
            SelfRecursive = selfRecursive,
            RecursionInLoop = recursionInLoop,
            UnguardedRecursion = unguardedRecursion,
            ParamCount = paramCount,
            MaxAccessDepth = maxAccessDepth,
            Lines = lines,
        };
    }

    private static void Walk(
        SyntaxNode node,
        SemanticModel semanticModel,
        IMethodSymbol? methodSymbol,
        int branchNesting,
        int loopNesting,
        bool insideConditional,
        ref int cyclomatic,
        ref int cognitive,
        ref int loopCount,
        ref int maxLoopDepth,
        ref int linearScanInLoop,
        ref int allocInLoop,
        ref bool selfRecursive,
        ref bool recursionInLoop,
        ref bool unguardedRecursion,
        ref int maxAccessDepth)
    {
        switch (node)
        {
            case IfStatementSyntax:
                cyclomatic++;
                cognitive += 1 + branchNesting;
                foreach (var child in node.ChildNodes())
                {
                    Walk(
                        child,
                        semanticModel,
                        methodSymbol,
                        branchNesting + 1,
                        loopNesting,
                        insideConditional: true,
                        ref cyclomatic,
                        ref cognitive,
                        ref loopCount,
                        ref maxLoopDepth,
                        ref linearScanInLoop,
                        ref allocInLoop,
                        ref selfRecursive,
                        ref recursionInLoop,
                        ref unguardedRecursion,
                        ref maxAccessDepth);
                }

                return;

            case ForStatementSyntax:
            case ForEachStatementSyntax:
            case WhileStatementSyntax:
            case DoStatementSyntax:
                cyclomatic++;
                loopCount++;
                cognitive += 1 + branchNesting + loopNesting;
                maxLoopDepth = Math.Max(maxLoopDepth, loopNesting + 1);
                foreach (var child in node.ChildNodes())
                {
                    Walk(
                        child,
                        semanticModel,
                        methodSymbol,
                        branchNesting,
                        loopNesting + 1,
                        insideConditional,
                        ref cyclomatic,
                        ref cognitive,
                        ref loopCount,
                        ref maxLoopDepth,
                        ref linearScanInLoop,
                        ref allocInLoop,
                        ref selfRecursive,
                        ref recursionInLoop,
                        ref unguardedRecursion,
                        ref maxAccessDepth);
                }

                return;

            case CatchClauseSyntax:
                cyclomatic++;
                cognitive += 1 + branchNesting;
                foreach (var child in node.ChildNodes())
                {
                    Walk(
                        child,
                        semanticModel,
                        methodSymbol,
                        branchNesting + 1,
                        loopNesting,
                        insideConditional,
                        ref cyclomatic,
                        ref cognitive,
                        ref loopCount,
                        ref maxLoopDepth,
                        ref linearScanInLoop,
                        ref allocInLoop,
                        ref selfRecursive,
                        ref recursionInLoop,
                        ref unguardedRecursion,
                        ref maxAccessDepth);
                }

                return;

            case SwitchSectionSyntax section:
                cyclomatic++;
                cognitive += 1 + branchNesting;
                foreach (var child in section.Statements)
                {
                    Walk(
                        child,
                        semanticModel,
                        methodSymbol,
                        branchNesting + 1,
                        loopNesting,
                        insideConditional,
                        ref cyclomatic,
                        ref cognitive,
                        ref loopCount,
                        ref maxLoopDepth,
                        ref linearScanInLoop,
                        ref allocInLoop,
                        ref selfRecursive,
                        ref recursionInLoop,
                        ref unguardedRecursion,
                        ref maxAccessDepth);
                }

                return;

            case ConditionalExpressionSyntax:
                cyclomatic++;
                cognitive += 1 + branchNesting;
                break;

            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression or (int)SyntaxKind.LogicalOrExpression }:
                cyclomatic++;
                cognitive++;
                break;

            case InvocationExpressionSyntax invocation:
                if (loopNesting > 0)
                {
                    var methodName = GetInvocationName(invocation);
                    if (LinearScanMethods.Contains(methodName))
                    {
                        linearScanInLoop++;
                    }

                    if (AllocMethods.Contains(methodName))
                    {
                        allocInLoop++;
                    }
                }

                if (IsSelfCall(semanticModel, methodSymbol, invocation))
                {
                    selfRecursive = true;
                    if (loopNesting > 0)
                    {
                        recursionInLoop = true;
                    }

                    if (!insideConditional)
                    {
                        unguardedRecursion = true;
                    }
                }

                break;

            case ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                if (loopNesting > 0)
                {
                    allocInLoop++;
                }

                break;

            case MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax:
                maxAccessDepth = Math.Max(maxAccessDepth, GetAccessDepth(node));
                break;
        }

        foreach (var child in node.ChildNodes())
        {
            Walk(
                child,
                semanticModel,
                methodSymbol,
                branchNesting,
                loopNesting,
                insideConditional,
                ref cyclomatic,
                ref cognitive,
                ref loopCount,
                ref maxLoopDepth,
                ref linearScanInLoop,
                ref allocInLoop,
                ref selfRecursive,
                ref recursionInLoop,
                ref unguardedRecursion,
                ref maxAccessDepth);
        }
    }

    private static BlockSyntax? GetExecutableBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Body,
            ConstructorDeclarationSyntax ctor => ctor.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null,
        };
    }

    private static ExpressionSyntax? GetExpressionBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax ctor => ctor.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => accessor.ExpressionBody?.Expression,
            _ => null,
        };
    }

    private static int CountLines(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return Math.Max(1, span.EndLinePosition.Line - span.StartLinePosition.Line + 1);
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => string.Empty,
        };
    }

    private static bool IsSelfCall(
        SemanticModel semanticModel,
        IMethodSymbol? methodSymbol,
        InvocationExpressionSyntax invocation)
    {
        if (methodSymbol is null)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        return target is IMethodSymbol called &&
            SymbolEqualityComparer.Default.Equals(called.ConstructedFrom ?? called, methodSymbol.ConstructedFrom ?? methodSymbol);
    }

    private static int GetAccessDepth(SyntaxNode node)
    {
        var depth = 0;
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax)
            {
                depth++;
            }
        }

        return depth;
    }
}

internal static class DefinitionPropertyBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildFile(int lines)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["complexity"] = 0,
            ["lines"] = lines,
            ["is_exported"] = false,
            ["is_test"] = false,
            ["is_entry_point"] = false,
        }, JsonOptions);
    }

    public static string Build(
        ISymbol symbol,
        CSharpComplexityMetrics? metrics,
        string? signature,
        string? returnType,
        string? parentClass)
    {
        var isExecutable = symbol is IMethodSymbol;
        var isExported = IsExported(symbol);
        var isTest = IsTestSymbol(symbol);
        var isEntryPoint = IsEntryPoint(symbol);

        if (isExecutable && metrics is not null)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["complexity"] = metrics.Complexity,
                ["cognitive"] = metrics.Cognitive,
                ["loop_count"] = metrics.LoopCount,
                ["loop_depth"] = metrics.LoopDepth,
                ["transitive_loop_depth"] = 0,
                ["self_recursive"] = metrics.SelfRecursive,
                ["recursive"] = metrics.SelfRecursive,
                ["param_count"] = metrics.ParamCount,
                ["max_access_depth"] = metrics.MaxAccessDepth,
                ["linear_scan_in_loop"] = metrics.LinearScanInLoop,
                ["alloc_in_loop"] = metrics.AllocInLoop,
                ["recursion_in_loop"] = metrics.RecursionInLoop,
                ["unguarded_recursion"] = metrics.UnguardedRecursion,
                ["lines"] = metrics.Lines,
                ["is_exported"] = isExported,
                ["is_test"] = isTest,
                ["is_entry_point"] = isEntryPoint,
                ["signature"] = signature,
                ["return_type"] = returnType,
                ["parent_class"] = parentClass,
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["complexity"] = 0,
            ["lines"] = metrics?.Lines ?? 1,
            ["is_exported"] = isExported,
            ["is_test"] = isTest,
            ["is_entry_point"] = isEntryPoint,
            ["parent_class"] = parentClass,
        }, JsonOptions);
    }

    private static bool IsExported(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Protected => true,
            Accessibility.ProtectedOrInternal => true,
            _ => false,
        };
    }

    private static bool IsTestSymbol(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name is "FactAttribute" or "TheoryAttribute" or "TestAttribute" or "TestMethodAttribute");
    }

    private static bool IsEntryPoint(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method ||
            method.Name != "Main" ||
            !method.IsStatic)
        {
            return false;
        }

        return method.ReturnsVoid || method.ReturnType.SpecialType == SpecialType.System_Int32;
    }
}
