using Cbm.Roslyn;

namespace Cbm.Tests;

public sealed class CbmRoslynTests
{
    [Fact]
    public async Task ProjectLoaderResolvesCrossFileInvocation()
    {
        using var temp = TempDirectory.Create();
        WriteFile(
            temp.Path,
            "Sample.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(
            temp.Path,
            "Caller.cs",
            """
            namespace Sample;

            public sealed class Caller
            {
                public string Run()
                {
                    var callee = new Callee();
                    return callee.Target();
                }
            }
            """);
        WriteFile(
            temp.Path,
            "Callee.cs",
            """
            namespace Sample;

            public sealed class Callee
            {
                public string Target()
                {
                    return "ok";
                }
            }
            """);

        var result = await new CSharpProjectLoader()
            .LoadAsync(System.IO.Path.Combine(temp.Path, "Sample.csproj"));
        var documents = result.Projects.Single().Documents;

        var declaredSymbols = documents
            .SelectMany(document => SymbolResolutionProbe.GetDeclaredSymbols(
                document.SemanticModel,
                document.SyntaxTree.GetRoot()))
            .Select(symbol => symbol.DisplayName)
            .ToArray();
        var invocationTargets = documents
            .SelectMany(document => SymbolResolutionProbe.ResolveInvocationTargets(
                document.SemanticModel,
                document.SyntaxTree.GetRoot()))
            .ToArray();

        Assert.Contains("Sample.Caller.Run()", declaredSymbols);
        Assert.Contains("Sample.Callee.Target()", declaredSymbols);
        Assert.Contains(
            invocationTargets,
            target => target.Expression == "callee.Target" &&
                target.TargetDisplayName == "Sample.Callee.Target()");
    }

    [Fact]
    public void LooseCompilationResolvesSameCompilationInvocation()
    {
        var loose = LooseCSharpCompilation.CreateFromSources(
            new Dictionary<string, string>
            {
                ["Loose.cs"] =
                    """
                    namespace Sample;

                    public static class Local
                    {
                        public static string Run()
                        {
                            return Target();
                        }

                        public static string Target()
                        {
                            return "ok";
                        }
                    }
                    """,
            });
        var document = loose.Documents.Single();

        var declaredSymbols = SymbolResolutionProbe
            .GetDeclaredSymbols(document.SemanticModel, document.SyntaxTree.GetRoot())
            .Select(symbol => symbol.DisplayName)
            .ToArray();
        var invocationTargets = SymbolResolutionProbe.ResolveInvocationTargets(
            document.SemanticModel,
            document.SyntaxTree.GetRoot());

        Assert.Contains("Sample.Local.Run()", declaredSymbols);
        Assert.Contains("Sample.Local.Target()", declaredSymbols);
        Assert.Contains(
            invocationTargets,
            target => target.Expression == "Target" &&
                target.TargetDisplayName == "Sample.Local.Target()");
    }

    [Fact]
    public void DiscoveryHonorsCSharpFilesAndIgnoreSources()
    {
        using var temp = TempDirectory.Create();
        WriteFile(temp.Path, ".gitignore", "IgnoredByGit.cs\nignored-dir/\n");
        WriteFile(temp.Path, ".git/info/exclude", "src/ExcludedByInfo.cs\n");
        WriteFile(temp.Path, ".cbmignore", "excluded-by-cbm/\n");
        WriteFile(temp.Path, "src/.gitignore", "NestedIgnored.cs\n");
        WriteFile(temp.Path, "src/Keep.cs", "namespace Sample; public sealed class Keep;\n");
        WriteFile(temp.Path, "src/Keep.txt", "not C#\n");
        WriteFile(temp.Path, "src/IgnoredByGit.cs", "namespace Sample; public sealed class Ignored;\n");
        WriteFile(temp.Path, "src/ExcludedByInfo.cs", "namespace Sample; public sealed class Excluded;\n");
        WriteFile(temp.Path, "src/NestedIgnored.cs", "namespace Sample; public sealed class NestedIgnored;\n");
        WriteFile(temp.Path, "src/nested/KeepNested.cs", "namespace Sample; public sealed class KeepNested;\n");
        WriteFile(temp.Path, "obj/Generated.cs", "namespace Sample; public sealed class Generated;\n");
        WriteFile(temp.Path, "vendor/Vendor.cs", "namespace Sample; public sealed class Vendor;\n");
        WriteFile(temp.Path, "ignored-dir/IgnoreMe.cs", "namespace Sample; public sealed class IgnoreMe;\n");
        WriteFile(temp.Path, "excluded-by-cbm/Cbm.cs", "namespace Sample; public sealed class Cbm;\n");

        var files = new CSharpDiscovery()
            .Discover(temp.Path)
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Equal(["src/Keep.cs", "src/nested/KeepNested.cs"], files);
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = System.IO.Path.Combine(
            root,
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cbm-roslyn-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
