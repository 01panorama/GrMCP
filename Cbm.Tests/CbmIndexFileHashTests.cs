using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmIndexFileHashTests
{
    [Fact]
    public async Task SecondIndexOnUnchangedRepoReportsUnchangedFilesAndStableCounts()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteFixture(temp.RootPath);

            var repository = new IndexRepository();
            var first = await repository.IndexAsync(temp.RootPath);
            Assert.True(first.FileChanges.New > 0);
            Assert.Equal(0, first.FileChanges.Unchanged);

            using (var store = CbmStore.OpenPath(first.DatabasePath))
            {
                var hashes = store.GetFileHashes(first.ProjectName);
                Assert.NotEmpty(hashes);
                Assert.All(hashes, hash => Assert.False(string.IsNullOrEmpty(hash.Sha256)));
            }

            var second = await repository.IndexAsync(temp.RootPath);
            Assert.Equal(IndexMode.NoChange, second.Mode);
            Assert.Equal(first.NodeCount, second.NodeCount);
            Assert.Equal(first.EdgeCount, second.EdgeCount);
            Assert.True(second.FileChanges.Unchanged > 0);
            Assert.Equal(0, second.FileChanges.Changed);
            Assert.Equal(0, second.FileChanges.New);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task ReindexPreservesAdrContent()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteFixture(temp.RootPath);
            var indexResult = await new IndexRepository().IndexAsync(temp.RootPath);
            const string adrContent = "## PURPOSE\nKeep this ADR across reindex.\n";

            var manageAdr = new ManageAdrService();
            manageAdr.Manage(indexResult.ProjectName, mode: "update", content: adrContent);

            await new IndexRepository().IndexAsync(temp.RootPath);

            var restored = manageAdr.Manage(indexResult.ProjectName, mode: "get");
            Assert.Equal(adrContent, restored.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task ReindexDropsDeletedFileHashRows()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteFixture(temp.RootPath);
            WriteFile(
                temp.RootPath,
                "tools/Helper.cs",
                """
                namespace Tools;

                public static class Helper
                {
                    public static string Run() => "helper";
                }
                """);

            var first = await new IndexRepository().IndexAsync(temp.RootPath);
            File.Delete(System.IO.Path.Combine(temp.RootPath, "tools/Helper.cs"));

            var second = await new IndexRepository().IndexAsync(temp.RootPath);
            Assert.Equal(1, second.FileChanges.Deleted);

            using var store = CbmStore.OpenPath(second.DatabasePath);
            Assert.DoesNotContain(
                store.GetFileHashes(second.ProjectName),
                hash => hash.RelativePath == "tools/Helper.cs");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static void WriteFixture(string root)
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
            "Worker.cs",
            """
            namespace Sample;

            public sealed class Worker
            {
                public string Execute() => "ok";
            }
            """);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(root, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static TempDirectory Create()
        {
            var rootPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cbm-index-filehash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
