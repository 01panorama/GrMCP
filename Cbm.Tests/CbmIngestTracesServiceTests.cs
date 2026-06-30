using System.Text.Json;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmIngestTracesServiceTests
{
    private const string Project = "ingest-traces-service";

    [Fact]
    public async Task Ingest_MatchesCallerCalleeAgainstIndexedGraph()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFixture(temp.Path);
            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var service = new IngestTracesService();

            var result = service.Ingest(
                indexResult.ProjectName,
                [ParseTrace("""{"caller":"Run","callee":"Target","duration_ms":12.5,"count":2}""")]);

            Assert.Equal("accepted", result.Status);
            Assert.Equal(1, result.TracesReceived);
            Assert.Equal(1, result.TracesIngested);
            Assert.Equal(1, result.EdgesMatched);
            Assert.Equal(0, result.Unresolved);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Ingest_AcceptsOtlpLikeHttpSpan()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFixture(temp.Path);
            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var service = new IngestTracesService();

            var result = service.Ingest(
                indexResult.ProjectName,
                [ParseTrace(
                    """
                    {
                      "resource": {
                        "attributes": [
                          { "key": "service.name", "string_value": "orders-api" }
                        ]
                      },
                      "attributes": [
                        { "key": "http.method", "string_value": "GET" },
                        { "key": "http.route", "string_value": "/orders" },
                        { "key": "http.status_code", "string_value": "200" }
                      ],
                      "start_time": "1000000000",
                      "end_time": "1500000000"
                    }
                    """)]);

            Assert.Equal(1, result.TracesIngested);
            Assert.Equal(0, result.Unresolved);

            using var store = CbmStore.OpenPath(CbmCachePaths.GetProjectDatabasePath(indexResult.ProjectName));
            var observations = store.ListRuntimeObservations(indexResult.ProjectName, limit: 1);
            Assert.Single(observations);
            Assert.Equal("orders-api", observations[0].Service);
            Assert.Equal("/orders", observations[0].Route);
            Assert.Equal("GET", observations[0].Method);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Ingest_UnresolvedSymbolsStillPersist()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFixture(temp.Path);
            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var service = new IngestTracesService();

            var result = service.Ingest(
                indexResult.ProjectName,
                [ParseTrace("""{"caller":"Missing","callee":"AlsoMissing","count":1}""")]);

            Assert.Equal(1, result.TracesIngested);
            Assert.Equal(1, result.Unresolved);
            Assert.Equal(0, result.EdgesMatched);

            using var store = CbmStore.OpenPath(CbmCachePaths.GetProjectDatabasePath(indexResult.ProjectName));
            Assert.Equal(1, store.CountRuntimeObservations(indexResult.ProjectName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Ingest_EmptyArray_ReturnsAcceptedWithZeroCounts()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/ingest-empty");
            var result = new IngestTracesService().Ingest(Project, Array.Empty<JsonElement>());

            Assert.Equal("accepted", result.Status);
            Assert.Equal(0, result.TracesReceived);
            Assert.Equal(0, result.TracesIngested);
            Assert.Equal(0, result.EdgesMatched);
            Assert.Equal(0, result.Unresolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void Ingest_UnparseableEntry_AddsWarningAndSkipsIngest()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            SeedProject("/tmp/ingest-partial");
            var result = new IngestTracesService().Ingest(
                Project,
                [ParseTrace("""{"service":"api-only"}""")]);

            Assert.Equal(1, result.TracesReceived);
            Assert.Equal(0, result.TracesIngested);
            Assert.Single(result.Warnings);
            Assert.Contains("caller, callee, or route", result.Warnings[0], StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static void SeedProject(string rootPath)
    {
        using var store = CbmStore.OpenPath(CbmCachePaths.GetProjectDatabasePath(Project));
        store.UpsertProject(Project, rootPath);
    }

    private static JsonElement ParseTrace(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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
                "cbm-ingest-tests",
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
