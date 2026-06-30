using Cbm.Pipeline;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmDetectChangesServiceTests
{
    [Fact]
    public async Task Detect_FilesScope_ReturnsOnlyChangedFiles()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        fixture.CheckoutFeatureAndEditCallee();

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "main",
            scope: "files");

        Assert.True(result.Success);
        Assert.Equal("files", result.Scope);
        Assert.Contains("Callee.cs", result.ChangedFiles);
        Assert.Empty(result.ChangedSymbols);
        Assert.Empty(result.ImpactedSymbols);
    }

    [Fact]
    public async Task Detect_SymbolsScope_ListsChangedFileSymbolsOnly()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        fixture.CheckoutFeatureAndEditCallee();

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "main",
            scope: "symbols");

        Assert.True(result.Success);
        Assert.Contains("Callee.cs", result.ChangedFiles);
        Assert.Contains(
            result.ChangedSymbols,
            symbol => symbol.Name == "Target" && symbol.File == "Callee.cs");
        Assert.DoesNotContain(result.ChangedSymbols, symbol => symbol.Name == "Run");
        Assert.Empty(result.ImpactedSymbols);
    }

    [Fact]
    public async Task Detect_ImpactScope_PropagatesCallers()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        fixture.CheckoutFeatureAndEditCallee();

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "main",
            scope: "impact",
            depth: 2);

        Assert.True(result.Success);
        Assert.Contains(
            result.ChangedSymbols,
            symbol => symbol.Name == "Target" && symbol.File == "Callee.cs");
        Assert.Contains(
            result.ImpactedSymbols,
            symbol => symbol.Name == "Run" &&
                symbol.Direction == "inbound" &&
                symbol.Hop == 1);
    }

    [Fact]
    public async Task Detect_SinceTakesPrecedenceOverBaseBranch()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "no-such-branch-xyz",
            since: "HEAD",
            scope: "files");

        Assert.True(result.Success);
        Assert.Equal("HEAD", result.Base);
    }

    [Fact]
    public async Task Detect_ReturnsNotAGitRepo()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var indexResult = await new IndexRepository().IndexAsync(temp.RootPath);

            var result = new DetectChangesService().Detect(indexResult.ProjectName, scope: "files");

            Assert.False(result.Success);
            Assert.Equal("not_a_git_repo", result.ErrorCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Detect_ReturnsInvalidRef()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "main;evil",
            scope: "files");

        Assert.False(result.Success);
        Assert.Equal("invalid_ref", result.ErrorCode);
    }

    [Fact]
    public async Task Detect_ReturnsBaseRefNotFound()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "no-such-branch-xyz",
            scope: "files");

        Assert.False(result.Success);
        Assert.Equal("base_ref_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task Detect_IncludesUnstagedWorkingTreeChanges()
    {
        using var fixture = await IndexedGitFixture.TryCreateAsync();
        if (fixture is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(fixture.RepositoryPath, "Callee.cs"),
            """
            namespace Sample;

            public sealed class Callee
            {
                public string Target() => "working-tree";
            }
            """);

        var result = new DetectChangesService().Detect(
            fixture.ProjectName,
            baseBranch: "main",
            scope: "files");

        Assert.True(result.Success);
        Assert.Contains("Callee.cs", result.ChangedFiles);
    }

    private static void WriteCallerCalleeFixture(string root)
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
        WriteFile(
            root,
            "Caller.cs",
            """
            namespace Sample;

            public sealed class Caller
            {
                public string Run() => new Callee().Target();
            }
            """);
        WriteFile(
            root,
            "Callee.cs",
            """
            namespace Sample;

            public sealed class Callee
            {
                public string Target() => "ok";
            }
            """);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private sealed class IndexedGitFixture : IDisposable
    {
        private IndexedGitFixture(string tempRoot, string repositoryPath, string projectName)
        {
            TempRoot = tempRoot;
            RepositoryPath = repositoryPath;
            ProjectName = projectName;
        }

        public string TempRoot { get; }

        public string RepositoryPath { get; }

        public string ProjectName { get; }

        public static async Task<IndexedGitFixture?> TryCreateAsync()
        {
            if (!GitProcessRunner.IsGitAvailable())
            {
                return null;
            }

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "cbm-detect-changes-" + Guid.NewGuid().ToString("N"));
            var repositoryPath = Path.Combine(tempRoot, "repo");
            Directory.CreateDirectory(repositoryPath);

            var cacheRoot = Path.Combine(tempRoot, "cache");
            Directory.CreateDirectory(cacheRoot);
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cacheRoot);

            var fixture = new IndexedGitFixture(tempRoot, repositoryPath, string.Empty);
            WriteCallerCalleeFixture(repositoryPath);
            fixture.RunGit("init");
            fixture.RunGit("checkout", "-b", "main");
            fixture.CommitAll("initial");

            var indexResult = await new IndexRepository().IndexAsync(repositoryPath);
            return new IndexedGitFixture(tempRoot, repositoryPath, indexResult.ProjectName);
        }

        public void CheckoutFeatureAndEditCallee()
        {
            RunGit("checkout", "-b", "feature/change");
            File.WriteAllText(
                Path.Combine(RepositoryPath, "Callee.cs"),
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "changed";
                }
                """);
            CommitAll("edit callee on feature");
        }

        public void RunGit(params string[] arguments)
        {
            var result = GitProcessRunner.RunInRepository(RepositoryPath, arguments);
            Assert.True(
                result.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed: {result.StandardError}");
        }

        public void CommitAll(string message)
        {
            RunGit("add", "-A");
            RunGit(
                "-c",
                "user.name=CBM Test",
                "-c",
                "user.email=cbm@example.invalid",
                "commit",
                "-m",
                message);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static TempDirectory Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cbm-detect-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
