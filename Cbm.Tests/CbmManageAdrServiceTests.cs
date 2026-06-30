using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmManageAdrServiceTests
{
    private const string Project = "manage-adr-service";

    [Fact]
    public void Manage_GetOnEmptyProject_ReturnsNoAdrHint()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/manage-adr-empty");

            var result = new ManageAdrService().Manage(Project);

            Assert.Equal(string.Empty, result.Content);
            Assert.Equal("no_adr", result.Status);
            Assert.Contains("No ADR yet", result.AdrHint, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Manage_UpdateAndGetRoundTrip()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/manage-adr-roundtrip");
            const string content = "## PURPOSE\nUnified ADR backend.\n";
            var service = new ManageAdrService();

            var updated = service.Manage(Project, mode: "update", content: content);
            var fetched = service.Manage(Project, mode: "get");

            Assert.Equal("updated", updated.Status);
            Assert.Equal(content, fetched.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Manage_SectionsListsStoredHeaders()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/manage-adr-sections");
            var service = new ManageAdrService();
            service.Manage(
                Project,
                mode: "update",
                content: "## PURPOSE\nWhy.\n\n## STACK\nC# + SQLite.\n");

            var result = service.Manage(Project, mode: "sections");

            Assert.NotNull(result.Sections);
            Assert.Contains("## PURPOSE", result.Sections);
            Assert.Contains("## STACK", result.Sections);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Manage_ImportsLegacyAdrFileOnFirstAccess()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFile(
                temp.Path,
                ".graph-mcp/adr.md",
                """
                ## PURPOSE
                Legacy ADR content.

                ## STACK
                Markdown file.
                """);

            SeedProject(temp.Path);

            var result = new ManageAdrService().Manage(Project, mode: "get");

            Assert.Contains("Legacy ADR content.", result.Content, StringComparison.Ordinal);

            using var store = CbmStore.OpenPath(CbmCachePaths.GetProjectDatabasePath(Project));
            Assert.True(store.AdrExists(Project));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Manage_UpdateWithoutContentFallsThroughToGet()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/manage-adr-fallthrough");
            var service = new ManageAdrService();
            service.Manage(Project, mode: "update", content: "## PURPOSE\nStored.\n");

            var result = service.Manage(Project, mode: "update", content: null);

            Assert.Equal("## PURPOSE\nStored.\n", result.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Manage_UnknownProject_Throws()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ManageAdrService().Manage("missing-project"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static void SeedProject(string rootPath)
    {
        var path = CbmCachePaths.GetProjectDatabasePath(Project);
        using var store = CbmStore.OpenPath(path);
        store.UpsertProject(Project, rootPath);
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
                "cbm-manage-adr-tests",
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
