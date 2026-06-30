using System.Text.Json;
using Cbm.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cbm.Roslyn;

public sealed class CSharpRelationshipExtractor
{
    public IReadOnlyList<CbmGraphEdge> ExtractFromLoadedDocuments(
        string projectName,
        string repoRoot,
        IEnumerable<CSharpLoadedDocument> documents,
        IReadOnlySet<string> knownQualifiedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(knownQualifiedNames);

        var state = new RelationshipState(projectName, knownQualifiedNames);
        foreach (var document in documents)
        {
            ExtractDocument(
                state,
                repoRoot,
                document.FilePath,
                document.SyntaxTree,
                document.SemanticModel);
        }

        return state.Edges;
    }

    public IReadOnlyList<CbmGraphEdge> ExtractFromLooseDocuments(
        string projectName,
        string repoRoot,
        IEnumerable<CSharpLooseDocument> documents,
        IReadOnlySet<string> knownQualifiedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(knownQualifiedNames);

        var state = new RelationshipState(projectName, knownQualifiedNames);
        foreach (var document in documents)
        {
            ExtractDocument(
                state,
                repoRoot,
                document.FilePath,
                document.SyntaxTree,
                document.SemanticModel);
        }

        return state.Edges;
    }

    private static void ExtractDocument(
        RelationshipState state,
        string repoRoot,
        string filePath,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel)
    {
        var relativePath = CSharpQualifiedName.ToRelativePath(repoRoot, filePath);
        var fileQualifiedName = CSharpQualifiedName.ForFile(state.ProjectName, relativePath);
        var root = syntaxTree.GetCompilationUnitRoot();

        ExtractImports(state, semanticModel, fileQualifiedName, root.Usings);

        foreach (var member in root.Members)
        {
            VisitMember(state, semanticModel, member, parentTypeQualifiedName: null);
        }
    }

    private static void VisitMember(
        RelationshipState state,
        SemanticModel semanticModel,
        SyntaxNode member,
        string? parentTypeQualifiedName)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                foreach (var child in namespaceDeclaration.Members)
                {
                    VisitMember(state, semanticModel, child, parentTypeQualifiedName: null);
                }

                return;

            case ClassDeclarationSyntax classDeclaration:
                VisitType(state, semanticModel, classDeclaration, parentTypeQualifiedName);
                return;

            case StructDeclarationSyntax structDeclaration:
                VisitType(state, semanticModel, structDeclaration, parentTypeQualifiedName);
                return;

            case RecordDeclarationSyntax recordDeclaration:
                VisitType(state, semanticModel, recordDeclaration, parentTypeQualifiedName);
                return;

            case InterfaceDeclarationSyntax interfaceDeclaration:
                VisitType(state, semanticModel, interfaceDeclaration, parentTypeQualifiedName);
                return;

            case EnumDeclarationSyntax enumDeclaration:
                VisitEnum(state, semanticModel, enumDeclaration, parentTypeQualifiedName);
                return;
        }
    }

    private static void VisitType(
        RelationshipState state,
        SemanticModel semanticModel,
        TypeDeclarationSyntax typeDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        var typeQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, symbol);
        ExtractTypeRelationships(state, symbol, typeQualifiedName);

        foreach (var member in typeDeclaration.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax methodDeclaration:
                    VisitExecutable(
                        state,
                        semanticModel,
                        methodDeclaration,
                        semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol,
                        typeQualifiedName);
                    break;

                case ConstructorDeclarationSyntax constructorDeclaration:
                    VisitExecutable(
                        state,
                        semanticModel,
                        constructorDeclaration,
                        semanticModel.GetDeclaredSymbol(constructorDeclaration) as IMethodSymbol,
                        typeQualifiedName);
                    break;

                case PropertyDeclarationSyntax propertyDeclaration:
                    VisitPropertyAccessorBodies(state, semanticModel, propertyDeclaration, typeQualifiedName);
                    break;

                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        if (variable.Initializer?.Value is not null)
                        {
                            VisitSyntax(
                                state,
                                semanticModel,
                                variable.Initializer.Value,
                                enclosingExecutable: null,
                                typeQualifiedName);
                        }
                    }

                    break;

                case TypeDeclarationSyntax nestedType:
                    VisitType(state, semanticModel, nestedType, typeQualifiedName);
                    break;
            }
        }
    }

    private static void VisitEnum(
        RelationshipState state,
        SemanticModel semanticModel,
        EnumDeclarationSyntax enumDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(enumDeclaration) as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        var typeQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, symbol);
        ExtractTypeRelationships(state, symbol, typeQualifiedName);
    }

    private static void VisitPropertyAccessorBodies(
        RelationshipState state,
        SemanticModel semanticModel,
        PropertyDeclarationSyntax propertyDeclaration,
        string typeQualifiedName)
    {
        foreach (var accessor in propertyDeclaration.AccessorList?.Accessors ?? [])
        {
            if (accessor.Body is not null)
            {
                VisitSyntax(
                    state,
                    semanticModel,
                    accessor.Body,
                    enclosingExecutable: null,
                    typeQualifiedName);
            }

            if (accessor.ExpressionBody?.Expression is not null)
            {
                VisitSyntax(
                    state,
                    semanticModel,
                    accessor.ExpressionBody.Expression,
                    enclosingExecutable: null,
                    typeQualifiedName);
            }
        }
    }

    private static void VisitExecutable(
        RelationshipState state,
        SemanticModel semanticModel,
        SyntaxNode executableDeclaration,
        IMethodSymbol? methodSymbol,
        string typeQualifiedName)
    {
        if (methodSymbol is null)
        {
            return;
        }

        var executableQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, methodSymbol);
        if (methodSymbol.OverriddenMethod is not null)
        {
            var overriddenQualifiedName = CSharpQualifiedName.ForSymbol(
                state.ProjectName,
                methodSymbol.OverriddenMethod);
            state.AddEdge(executableQualifiedName, overriddenQualifiedName, "OVERRIDES");
        }

        var body = executableDeclaration switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax ctor => (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody?.Expression,
            _ => null,
        };

        if (body is not null)
        {
            VisitSyntax(state, semanticModel, body, executableQualifiedName, typeQualifiedName);
        }
    }

    private static void ExtractTypeRelationships(
        RelationshipState state,
        INamedTypeSymbol symbol,
        string typeQualifiedName)
    {
        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            var baseQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, baseType);
            state.AddEdge(typeQualifiedName, baseQualifiedName, "INHERITS");
        }

        foreach (var iface in symbol.Interfaces)
        {
            var interfaceQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, iface);
            state.AddEdge(typeQualifiedName, interfaceQualifiedName, "IMPLEMENTS");
        }
    }

    private static void ExtractImports(
        RelationshipState state,
        SemanticModel semanticModel,
        string fileQualifiedName,
        SyntaxList<UsingDirectiveSyntax> usings)
    {
        foreach (var usingDirective in usings)
        {
            if (usingDirective.Name is null)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(usingDirective.Name);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol is null)
            {
                continue;
            }

            var targetQualifiedName = ResolveImportTarget(state.ProjectName, symbol);
            if (targetQualifiedName is not null)
            {
                state.AddEdge(fileQualifiedName, targetQualifiedName, "IMPORTS");
            }
        }
    }

    private static string? ResolveImportTarget(string projectName, ISymbol symbol)
    {
        return symbol switch
        {
            INamespaceSymbol namespaceSymbol => CSharpQualifiedName.ForSymbol(projectName, namespaceSymbol),
            INamedTypeSymbol namedTypeSymbol => CSharpQualifiedName.ForSymbol(projectName, namedTypeSymbol),
            IAliasSymbol { Target: INamespaceSymbol aliasNamespace } =>
                CSharpQualifiedName.ForSymbol(projectName, aliasNamespace),
            IAliasSymbol { Target: INamedTypeSymbol aliasType } =>
                CSharpQualifiedName.ForSymbol(projectName, aliasType),
            _ => null,
        };
    }

    private static void VisitSyntax(
        RelationshipState state,
        SemanticModel semanticModel,
        SyntaxNode root,
        string? enclosingExecutable,
        string? typeQualifiedName,
        int branchNesting = 0,
        int loopNesting = 0)
    {
        switch (root)
        {
            case InvocationExpressionSyntax invocation:
                ExtractCall(
                    state,
                    semanticModel,
                    invocation,
                    enclosingExecutable,
                    loopNesting,
                    branchNesting);
                break;

            case ObjectCreationExpressionSyntax objectCreation:
                ExtractObjectCreationCall(
                    state,
                    semanticModel,
                    objectCreation,
                    enclosingExecutable,
                    loopNesting,
                    branchNesting);
                break;

            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                ExtractImplicitObjectCreationCall(
                    state,
                    semanticModel,
                    implicitCreation,
                    enclosingExecutable,
                    loopNesting,
                    branchNesting);
                break;

            case IdentifierNameSyntax identifier when !IsDeclarationName(identifier):
                ExtractUsage(
                    state,
                    semanticModel,
                    identifier,
                    enclosingExecutable,
                    typeQualifiedName,
                    isWrite: false);
                break;

            case AssignmentExpressionSyntax assignment:
                ExtractWrite(state, semanticModel, assignment.Left, enclosingExecutable, typeQualifiedName);
                foreach (var child in assignment.ChildNodes())
                {
                    if (ReferenceEquals(child, assignment.Left))
                    {
                        continue;
                    }

                    VisitChild(state, semanticModel, child, enclosingExecutable, typeQualifiedName, branchNesting, loopNesting);
                }

                return;

            case ForStatementSyntax:
            case ForEachStatementSyntax:
            case WhileStatementSyntax:
            case DoStatementSyntax:
                foreach (var child in root.ChildNodes())
                {
                    VisitChild(
                        state,
                        semanticModel,
                        child,
                        enclosingExecutable,
                        typeQualifiedName,
                        branchNesting,
                        loopNesting + 1);
                }

                return;

            case IfStatementSyntax:
            case CatchClauseSyntax:
            case SwitchSectionSyntax:
                foreach (var child in root.ChildNodes())
                {
                    VisitChild(
                        state,
                        semanticModel,
                        child,
                        enclosingExecutable,
                        typeQualifiedName,
                        branchNesting + 1,
                        loopNesting);
                }

                return;
        }

        foreach (var child in root.ChildNodes())
        {
            VisitChild(state, semanticModel, child, enclosingExecutable, typeQualifiedName, branchNesting, loopNesting);
        }
    }

    private static void VisitChild(
        RelationshipState state,
        SemanticModel semanticModel,
        SyntaxNode child,
        string? enclosingExecutable,
        string? typeQualifiedName,
        int branchNesting,
        int loopNesting)
    {
        VisitSyntax(state, semanticModel, child, enclosingExecutable, typeQualifiedName, branchNesting, loopNesting);
    }

    private static void ExtractCall(
        RelationshipState state,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        string? enclosingExecutable,
        int loopNesting,
        int branchNesting)
    {
        if (string.IsNullOrEmpty(enclosingExecutable))
        {
            return;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (target is not IMethodSymbol method)
        {
            return;
        }

        var targetQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, method);
        state.AddCallEdge(
            enclosingExecutable,
            targetQualifiedName,
            invocation.ArgumentList.Arguments.Count,
            loopNesting,
            branchNesting);
    }

    private static void ExtractObjectCreationCall(
        RelationshipState state,
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax objectCreation,
        string? enclosingExecutable,
        int loopNesting,
        int branchNesting)
    {
        if (string.IsNullOrEmpty(enclosingExecutable))
        {
            return;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(objectCreation);
        var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (target is not IMethodSymbol constructor)
        {
            return;
        }

        var targetQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, constructor);
        state.AddCallEdge(
            enclosingExecutable,
            targetQualifiedName,
            objectCreation.ArgumentList?.Arguments.Count ?? 0,
            loopNesting,
            branchNesting);
    }

    private static void ExtractImplicitObjectCreationCall(
        RelationshipState state,
        SemanticModel semanticModel,
        ImplicitObjectCreationExpressionSyntax implicitCreation,
        string? enclosingExecutable,
        int loopNesting,
        int branchNesting)
    {
        if (string.IsNullOrEmpty(enclosingExecutable))
        {
            return;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(implicitCreation);
        var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (target is not IMethodSymbol constructor)
        {
            return;
        }

        var targetQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, constructor);
        state.AddCallEdge(
            enclosingExecutable,
            targetQualifiedName,
            implicitCreation.ArgumentList.Arguments.Count,
            loopNesting,
            branchNesting);
    }

    private static void ExtractUsage(
        RelationshipState state,
        SemanticModel semanticModel,
        IdentifierNameSyntax identifier,
        string? enclosingExecutable,
        string? typeQualifiedName,
        bool isWrite)
    {
        var source = enclosingExecutable ?? typeQualifiedName;
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (!IsUsageTarget(symbol))
        {
            return;
        }

        var targetQualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, symbol!);
        state.AddEdge(source, targetQualifiedName, isWrite ? "WRITES" : "USAGE");
    }

    private static void ExtractWrite(
        RelationshipState state,
        SemanticModel semanticModel,
        ExpressionSyntax left,
        string? enclosingExecutable,
        string? typeQualifiedName)
    {
        switch (left)
        {
            case IdentifierNameSyntax identifier:
                ExtractUsage(state, semanticModel, identifier, enclosingExecutable, typeQualifiedName, isWrite: true);
                break;

            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name is IdentifierNameSyntax name:
                ExtractUsage(state, semanticModel, name, enclosingExecutable, typeQualifiedName, isWrite: true);
                break;
        }
    }

    private static bool IsUsageTarget(ISymbol? symbol)
    {
        return symbol is IFieldSymbol or IPropertySymbol or ILocalSymbol or IParameterSymbol;
    }

    private static bool IsDeclarationName(IdentifierNameSyntax identifier)
    {
        return identifier.Parent switch
        {
            VariableDeclaratorSyntax => true,
            ParameterSyntax => true,
            MethodDeclarationSyntax => true,
            ClassDeclarationSyntax => true,
            StructDeclarationSyntax => true,
            InterfaceDeclarationSyntax => true,
            EnumDeclarationSyntax => true,
            EnumMemberDeclarationSyntax => true,
            PropertyDeclarationSyntax => true,
            EventDeclarationSyntax => true,
            DelegateDeclarationSyntax => true,
            TypeParameterSyntax => true,
            _ => false,
        };
    }

    private sealed class RelationshipState(string projectName, IReadOnlySet<string> knownQualifiedNames)
    {
        private readonly List<CbmGraphEdge> edges = [];
        private readonly HashSet<(string Source, string Target, string Type)> seenEdges = new();

        public string ProjectName { get; } = projectName;

        public IReadOnlyList<CbmGraphEdge> Edges => edges;

        public void AddEdge(string sourceQualifiedName, string targetQualifiedName, string type)
        {
            if (!knownQualifiedNames.Contains(sourceQualifiedName) ||
                !knownQualifiedNames.Contains(targetQualifiedName))
            {
                return;
            }

            if (!seenEdges.Add((sourceQualifiedName, targetQualifiedName, type)))
            {
                return;
            }

            edges.Add(new CbmGraphEdge
            {
                Project = ProjectName,
                SourceQualifiedName = sourceQualifiedName,
                TargetQualifiedName = targetQualifiedName,
                Type = type,
                PropertiesJson = RelationshipPropertyBuilder.BuildDefault(),
            });
        }

        public void AddCallEdge(
            string sourceQualifiedName,
            string targetQualifiedName,
            int argCount,
            int loopDepth,
            int branchDepth)
        {
            if (!knownQualifiedNames.Contains(sourceQualifiedName) ||
                !knownQualifiedNames.Contains(targetQualifiedName))
            {
                return;
            }

            if (!seenEdges.Add((sourceQualifiedName, targetQualifiedName, "CALLS")))
            {
                return;
            }

            edges.Add(new CbmGraphEdge
            {
                Project = ProjectName,
                SourceQualifiedName = sourceQualifiedName,
                TargetQualifiedName = targetQualifiedName,
                Type = "CALLS",
                PropertiesJson = RelationshipPropertyBuilder.BuildCall(argCount, loopDepth, branchDepth),
            });
        }
    }
}

internal static class RelationshipPropertyBuilder
{
    public static string BuildDefault()
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["confidence"] = 1.0,
            ["strategy"] = "roslyn",
        });
    }

    public static string BuildCall(int argCount, int loopDepth, int branchDepth)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["confidence"] = 1.0,
            ["strategy"] = "roslyn",
            ["arg_count"] = argCount,
            ["loop_depth"] = loopDepth,
            ["branch_depth"] = branchDepth,
        });
    }
}
