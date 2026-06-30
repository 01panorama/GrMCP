using Cbm.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cbm.Roslyn;

public sealed class CSharpDefinitionExtractor
{
    public CbmDefinitionExtractionResult ExtractFromLoadedDocuments(
        string projectName,
        string repoRoot,
        IEnumerable<CSharpLoadedDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(documents);

        var state = new ExtractionState(projectName);
        foreach (var document in documents)
        {
            ExtractDocument(
                state,
                repoRoot,
                document.FilePath,
                document.SyntaxTree,
                document.SemanticModel);
        }

        return state.ToResult();
    }

    public CbmDefinitionExtractionResult ExtractFromLooseDocuments(
        string projectName,
        string repoRoot,
        IEnumerable<CSharpLooseDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(documents);

        var state = new ExtractionState(projectName);
        foreach (var document in documents)
        {
            ExtractDocument(
                state,
                repoRoot,
                document.FilePath,
                document.SyntaxTree,
                document.SemanticModel);
        }

        return state.ToResult();
    }

    private static void ExtractDocument(
        ExtractionState state,
        string repoRoot,
        string filePath,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel)
    {
        var relativePath = CSharpQualifiedName.ToRelativePath(repoRoot, filePath);
        var fileQualifiedName = CSharpQualifiedName.ForFile(state.ProjectName, relativePath);
        var root = syntaxTree.GetCompilationUnitRoot();
        var fileSpan = root.GetLocation().GetLineSpan();

        state.AddNode(
            label: "File",
            name: Path.GetFileNameWithoutExtension(relativePath),
            qualifiedName: fileQualifiedName,
            filePath: relativePath,
            startLine: fileSpan.StartLinePosition.Line + 1,
            endLine: fileSpan.EndLinePosition.Line + 1,
            propertiesJson: DefinitionPropertyBuilder.BuildFile(
                Math.Max(1, fileSpan.EndLinePosition.Line - fileSpan.StartLinePosition.Line + 1)));

        state.PushScope(fileQualifiedName);
        foreach (var member in root.Members)
        {
            VisitMember(state, semanticModel, relativePath, member, parentTypeQualifiedName: null);
        }

        state.PopScope();
    }

    private static void VisitMember(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        SyntaxNode member,
        string? parentTypeQualifiedName)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                VisitNamespace(state, semanticModel, relativePath, namespaceDeclaration);
                return;

            case ClassDeclarationSyntax classDeclaration:
                VisitType(state, semanticModel, relativePath, classDeclaration, "Class", parentTypeQualifiedName);
                return;

            case StructDeclarationSyntax structDeclaration:
                VisitType(state, semanticModel, relativePath, structDeclaration, "Struct", parentTypeQualifiedName);
                return;

            case RecordDeclarationSyntax recordDeclaration:
                VisitType(state, semanticModel, relativePath, recordDeclaration, "Record", parentTypeQualifiedName);
                return;

            case InterfaceDeclarationSyntax interfaceDeclaration:
                VisitType(state, semanticModel, relativePath, interfaceDeclaration, "Interface", parentTypeQualifiedName);
                return;

            case EnumDeclarationSyntax enumDeclaration:
                VisitEnum(state, semanticModel, relativePath, enumDeclaration, parentTypeQualifiedName);
                return;

            case DelegateDeclarationSyntax delegateDeclaration:
                VisitDelegate(state, semanticModel, relativePath, delegateDeclaration, parentTypeQualifiedName);
                return;

            case MethodDeclarationSyntax methodDeclaration:
                VisitMethod(state, semanticModel, relativePath, methodDeclaration, parentTypeQualifiedName);
                return;

            case ConstructorDeclarationSyntax constructorDeclaration:
                VisitConstructor(state, semanticModel, relativePath, constructorDeclaration, parentTypeQualifiedName);
                return;

            case PropertyDeclarationSyntax propertyDeclaration:
                VisitProperty(state, semanticModel, relativePath, propertyDeclaration, parentTypeQualifiedName);
                return;

            case FieldDeclarationSyntax fieldDeclaration:
                VisitField(state, semanticModel, relativePath, fieldDeclaration, parentTypeQualifiedName);
                return;

            case EventDeclarationSyntax eventDeclaration:
                VisitEvent(state, semanticModel, relativePath, eventDeclaration, parentTypeQualifiedName);
                return;

            case EventFieldDeclarationSyntax eventFieldDeclaration:
                VisitEventField(state, semanticModel, relativePath, eventFieldDeclaration, parentTypeQualifiedName);
                return;

            case EnumMemberDeclarationSyntax enumMemberDeclaration:
                VisitEnumMember(state, semanticModel, relativePath, enumMemberDeclaration, parentTypeQualifiedName);
                return;
        }
    }

    private static void VisitNamespace(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        BaseNamespaceDeclarationSyntax namespaceDeclaration)
    {
        var symbol = semanticModel.GetDeclaredSymbol(namespaceDeclaration);
        if (symbol is null)
        {
            return;
        }

        AddSymbolNode(state, semanticModel, relativePath, symbol, "Namespace", parentTypeQualifiedName: null);
        state.PushScope(CSharpQualifiedName.ForSymbol(state.ProjectName, symbol));
        foreach (var member in namespaceDeclaration.Members)
        {
            VisitMember(state, semanticModel, relativePath, member, parentTypeQualifiedName: null);
        }

        state.PopScope();
    }

    private static void VisitType(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        TypeDeclarationSyntax typeDeclaration,
        string label,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        var qualifiedName = AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            label,
            parentTypeQualifiedName);

        state.PushScope(qualifiedName);
        foreach (var member in typeDeclaration.Members)
        {
            VisitMember(state, semanticModel, relativePath, member, qualifiedName);
        }

        state.PopScope();
    }

    private static void VisitEnum(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        EnumDeclarationSyntax enumDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(enumDeclaration) as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        var qualifiedName = AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "Enum",
            parentTypeQualifiedName);

        state.PushScope(qualifiedName);
        foreach (var member in enumDeclaration.Members)
        {
            VisitMember(state, semanticModel, relativePath, member, qualifiedName);
        }

        state.PopScope();
    }

    private static void VisitDelegate(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        DelegateDeclarationSyntax delegateDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(delegateDeclaration);
        if (symbol is null)
        {
            return;
        }

        AddSymbolNode(state, semanticModel, relativePath, symbol, "Delegate", parentTypeQualifiedName);
    }

    private static void VisitMethod(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        MethodDeclarationSyntax methodDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        if (symbol is null)
        {
            return;
        }

        var metrics = CSharpComplexityAnalyzer.Analyze(semanticModel, methodDeclaration, symbol);
        AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "Method",
            parentTypeQualifiedName,
            metrics,
            GetSignature(symbol),
            symbol.ReturnType.ToDisplayString());
        VisitLocals(state, semanticModel, relativePath, methodDeclaration.Body, methodDeclaration.ExpressionBody);
    }

    private static void VisitConstructor(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        ConstructorDeclarationSyntax constructorDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(constructorDeclaration) as IMethodSymbol;
        if (symbol is null)
        {
            return;
        }

        var metrics = CSharpComplexityAnalyzer.Analyze(semanticModel, constructorDeclaration, symbol);
        AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "Constructor",
            parentTypeQualifiedName,
            metrics,
            GetSignature(symbol),
            returnType: null);
        VisitLocals(state, semanticModel, relativePath, constructorDeclaration.Body, constructorDeclaration.ExpressionBody);
    }

    private static void VisitProperty(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        PropertyDeclarationSyntax propertyDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol;
        if (symbol is null)
        {
            return;
        }

        AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "Property",
            parentTypeQualifiedName,
            metrics: null,
            signature: null,
            symbol.Type.ToDisplayString());
    }

    private static void VisitField(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        FieldDeclarationSyntax fieldDeclaration,
        string? parentTypeQualifiedName)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            var symbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (symbol is null)
            {
                continue;
            }

            AddSymbolNode(
                state,
                semanticModel,
                relativePath,
                symbol,
                "Field",
                parentTypeQualifiedName,
                metrics: null,
                signature: null,
                symbol.Type.ToDisplayString());
        }
    }

    private static void VisitEvent(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        EventDeclarationSyntax eventDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(eventDeclaration) as IEventSymbol;
        if (symbol is null)
        {
            return;
        }

        AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "Event",
            parentTypeQualifiedName,
            metrics: null,
            signature: null,
            symbol.Type.ToDisplayString());
    }

    private static void VisitEventField(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        EventFieldDeclarationSyntax eventFieldDeclaration,
        string? parentTypeQualifiedName)
    {
        foreach (var variable in eventFieldDeclaration.Declaration.Variables)
        {
            var symbol = semanticModel.GetDeclaredSymbol(variable) as IEventSymbol;
            if (symbol is null)
            {
                continue;
            }

            AddSymbolNode(
                state,
                semanticModel,
                relativePath,
                symbol,
                "Event",
                parentTypeQualifiedName,
                metrics: null,
                signature: null,
                symbol.Type.ToDisplayString());
        }
    }

    private static void VisitEnumMember(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        EnumMemberDeclarationSyntax enumMemberDeclaration,
        string? parentTypeQualifiedName)
    {
        var symbol = semanticModel.GetDeclaredSymbol(enumMemberDeclaration) as IFieldSymbol;
        if (symbol is null)
        {
            return;
        }

        AddSymbolNode(
            state,
            semanticModel,
            relativePath,
            symbol,
            "EnumMember",
            parentTypeQualifiedName,
            metrics: null,
            signature: null,
            symbol.Type.ToDisplayString());
    }

    private static void VisitLocals(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody)
    {
        var root = (SyntaxNode?)body ?? expressionBody;
        if (root is null)
        {
            return;
        }

        foreach (var localDeclaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                if (symbol is null)
                {
                    continue;
                }

                AddSymbolNode(
                    state,
                    semanticModel,
                    relativePath,
                    symbol,
                    "Variable",
                    parentTypeQualifiedName: state.CurrentScopeQualifiedName,
                    metrics: null,
                    signature: null,
                    symbol.Type.ToDisplayString());
            }
        }
    }

    private static string AddSymbolNode(
        ExtractionState state,
        SemanticModel semanticModel,
        string relativePath,
        ISymbol symbol,
        string label,
        string? parentTypeQualifiedName,
        CSharpComplexityMetrics? metrics = null,
        string? signature = null,
        string? returnType = null)
    {
        var qualifiedName = CSharpQualifiedName.ForSymbol(state.ProjectName, symbol);
        var span = symbol.Locations.FirstOrDefault()?.GetLineSpan() ??
            semanticModel.SyntaxTree.GetRoot().GetLocation().GetLineSpan();
        var parentClass = parentTypeQualifiedName;

        state.AddNode(
            label,
            symbol.Name,
            qualifiedName,
            relativePath,
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            DefinitionPropertyBuilder.Build(symbol, metrics, signature, returnType, parentClass));

        var parentQualifiedName = state.CurrentScopeQualifiedName;
        if (!string.IsNullOrEmpty(parentQualifiedName))
        {
            var edgeType = label is "Method" or "Constructor" ? "DEFINES_METHOD" : "DEFINES";
            state.AddEdge(parentQualifiedName, qualifiedName, edgeType);
        }

        return qualifiedName;
    }

    private static string GetSignature(IMethodSymbol method)
    {
        var parameters = string.Join(
            ", ",
            method.Parameters.Select(parameter => $"{parameter.Type.ToDisplayString()} {parameter.Name}"));
        return $"({parameters})";
    }

    private sealed class ExtractionState(string projectName)
    {
        private readonly List<CbmNode> nodes = [];
        private readonly List<CbmGraphEdge> edges = [];
        private readonly HashSet<string> seenQualifiedNames = new(StringComparer.Ordinal);
        private readonly Stack<string> scopeStack = new();

        public string ProjectName { get; } = projectName;

        public string? CurrentScopeQualifiedName => scopeStack.Count > 0 ? scopeStack.Peek() : null;

        public void PushScope(string qualifiedName) => scopeStack.Push(qualifiedName);

        public void PopScope()
        {
            if (scopeStack.Count > 0)
            {
                scopeStack.Pop();
            }
        }

        public void AddNode(
            string label,
            string name,
            string qualifiedName,
            string filePath,
            int startLine,
            int endLine,
            string propertiesJson)
        {
            if (!seenQualifiedNames.Add(qualifiedName))
            {
                return;
            }

            nodes.Add(new CbmNode
            {
                Project = ProjectName,
                Label = label,
                Name = name,
                QualifiedName = qualifiedName,
                FilePath = filePath,
                StartLine = startLine,
                EndLine = endLine,
                PropertiesJson = propertiesJson,
            });
        }

        public void AddEdge(string sourceQualifiedName, string targetQualifiedName, string type)
        {
            edges.Add(new CbmGraphEdge
            {
                Project = ProjectName,
                SourceQualifiedName = sourceQualifiedName,
                TargetQualifiedName = targetQualifiedName,
                Type = type,
            });
        }

        public CbmDefinitionExtractionResult ToResult() => new(nodes, edges);
    }
}
