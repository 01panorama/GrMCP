using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Cbm.Roslyn;

public static class LooseCSharpCompilation
{
    public static CSharpLooseCompilation CreateFromFiles(
        IEnumerable<string> filePaths,
        string assemblyName = "Cbm.LooseCompilation")
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        var syntaxTrees = filePaths
            .Select(path => Path.GetFullPath(path))
            .Select(path => CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(path), Encoding.UTF8),
                path: path))
            .ToArray();

        return Create(syntaxTrees, assemblyName);
    }

    public static CSharpLooseCompilation CreateFromSources(
        IReadOnlyDictionary<string, string> sources,
        string assemblyName = "Cbm.LooseCompilation")
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Value, Encoding.UTF8),
                path: source.Key))
            .ToArray();

        return Create(syntaxTrees, assemblyName);
    }

    private static CSharpLooseCompilation Create(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var documents = syntaxTrees
            .Select(tree => new CSharpLooseDocument(
                tree.FilePath,
                tree,
                compilation.GetSemanticModel(tree, ignoreAccessibility: true)))
            .ToArray();

        return new CSharpLooseCompilation(compilation, documents);
    }

    private static IReadOnlyList<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}

public sealed record CSharpLooseCompilation(
    CSharpCompilation Compilation,
    IReadOnlyList<CSharpLooseDocument> Documents);

public sealed record CSharpLooseDocument(
    string FilePath,
    SyntaxTree SyntaxTree,
    SemanticModel SemanticModel);
