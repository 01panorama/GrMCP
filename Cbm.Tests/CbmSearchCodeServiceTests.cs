using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmSearchCodeServiceTests
{
    private const string Project = "search-code-service";

    [Fact]
    public async Task Search_DedupesLiteralPatternToContainingMethod()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteProject(temp.Path);
            WriteFile(
                temp.Path,
                "Worker.cs",
                """
                namespace Sample;

                public sealed class Worker
                {
                    public string Execute()
                    {
                        return "ok";
                    }
                }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var result = new SearchCodeService().Search(indexResult.ProjectName, "Execute");

            Assert.True(result.TotalResults >= 1);
            var hit = result.Results[0];
            Assert.Equal("Execute", hit.Node);
            Assert.Equal("Method", hit.Label);
            Assert.Contains(hit.MatchLines, line => line > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Search_FilesModeReturnsDistinctPaths()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteProject(temp.Path);
            WriteFile(
                temp.Path,
                "Caller.cs",
                """
                namespace Sample;

                public sealed class Caller
                {
                    public string Run() => new Callee().Target();
                }
                """);
            WriteFile(
                temp.Path,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "ok";
                }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var result = new SearchCodeService().Search(
                indexResult.ProjectName,
                "Target",
                mode: "files");

            Assert.NotNull(result.Files);
            Assert.Contains(result.Files, file => file.EndsWith("Callee.cs", StringComparison.Ordinal));
            Assert.Empty(result.Results);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Search_FilePatternAndPathFilterNarrowResults()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteProject(temp.Path);
            WriteFile(
                temp.Path,
                "Caller.cs",
                """
                namespace Sample;

                public sealed class Caller
                {
                    public string Run() => new Callee().Target();
                }
                """);
            WriteFile(
                temp.Path,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "ok";
                }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var filtered = new SearchCodeService().Search(
                indexResult.ProjectName,
                "Target",
                filePattern: "Callee.cs",
                pathFilter: "^Callee\\.cs$");

            Assert.Equal(1, filtered.TotalResults);
            Assert.Equal("Callee.cs", filtered.Results[0].File);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Search_PutsUnmappedHitsInRawMatches()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFile(
                temp.Path,
                "Orphan.cs",
                """
                // orphan marker XYZ123
                """);

            SeedProject(temp.Path, store =>
            {
                store.UpsertNode(new CbmNode
                {
                    Project = Project,
                    Label = "Method",
                    Name = "Later",
                    QualifiedName = $"{Project}.Orphan.Later",
                    FilePath = "Orphan.cs",
                    StartLine = 10,
                    EndLine = 20,
                });
            });

            var result = new SearchCodeService().Search(Project, "orphan marker XYZ123");

            Assert.Equal(0, result.TotalResults);
            Assert.Equal(1, result.RawMatchCount);
            Assert.Contains(result.RawMatches, raw => raw.File == "Orphan.cs" && raw.Line == 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Search_InvalidRegexPatternThrows()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject(null, store =>
            {
                store.UpsertNode(MethodNode("noop", $"{Project}.noop", "noop.cs", 1, 5));
            });

            var exception = Assert.Throws<ArgumentException>(() =>
                new SearchCodeService().Search(Project, "(unclosed", useRegex: true));

            Assert.Contains("invalid regex pattern", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Search_InvalidRootPathReportsRootPath()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/search;code-service", store =>
            {
                store.UpsertNode(MethodNode("noop", $"{Project}.noop", "noop.cs", 1, 5));
            });

            var exception = Assert.Throws<ArgumentException>(() =>
                new SearchCodeService().Search(Project, "noop"));

            Assert.Contains("root_path contains invalid characters", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Search_RanksHigherFanInMethodFirst()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFile(
                temp.Path,
                "Rank.cs",
                """
                namespace Sample;

                public sealed class Rank
                {
                    public void Popular() { }
                    public void Obscure() { }
                }
                """);

            SeedProject(temp.Path, store =>
            {
                var popularId = store.UpsertNode(MethodNode("Popular", $"{Project}.Rank.Popular", "Rank.cs", 5, 5));
                var obscureId = store.UpsertNode(MethodNode("Obscure", $"{Project}.Rank.Obscure", "Rank.cs", 6, 6));
                store.UpsertEdge(new CbmEdge { Project = Project, SourceId = popularId, TargetId = popularId, Type = "CALLS" });
                store.UpsertEdge(new CbmEdge { Project = Project, SourceId = obscureId, TargetId = popularId, Type = "CALLS" });
            });

            var result = new SearchCodeService().Search(Project, "public void");

            Assert.True(result.TotalResults >= 2);
            Assert.Equal("Popular", result.Results[0].Node);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static CbmNode MethodNode(string name, string qualifiedName, string file, int start, int end)
    {
        return new CbmNode
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = file,
            StartLine = start,
            EndLine = end,
        };
    }

    private static void SeedProject(string? rootPath, Action<CbmStore> seed)
    {
        var path = CbmCachePaths.GetProjectDatabasePath(Project);
        using var store = CbmStore.OpenPath(path);
        store.UpsertProject(Project, rootPath ?? "/tmp/search-code-service");
        seed(store);
    }

    private static void WriteProject(string root)
    {
        WriteFile(
            root,
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
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
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
                "cbm-search-code-tests",
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
