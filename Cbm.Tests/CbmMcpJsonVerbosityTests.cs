using System.Text.Json;
using Cbm.Graph;
using Cbm.Mcp;
using Cbm.Pipeline;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmMcpJsonVerbosityTests
{
    private const string Project = "verbosity-search";
    private const string PropertyBagJson =
        """
        {"complexity":4,"cognitive":3,"linear_scan_in_loop":true,"signature":"()","return_type":"string","parent_class":"Worker"}
        """;

    [Fact]
    public void Parse_BlankUnknownAndFull_AreFull_CompactIsCompact()
    {
        Assert.Equal(CbmVerbosity.Full, CbmVerbosityParser.Parse(null));
        Assert.Equal(CbmVerbosity.Full, CbmVerbosityParser.Parse(""));
        Assert.Equal(CbmVerbosity.Full, CbmVerbosityParser.Parse("full"));
        Assert.Equal(CbmVerbosity.Full, CbmVerbosityParser.Parse("unknown"));
        Assert.Equal(CbmVerbosity.Compact, CbmVerbosityParser.Parse("compact"));
        Assert.Equal(CbmVerbosity.Compact, CbmVerbosityParser.Parse(" COMPACT "));
    }

    [Fact]
    public void SearchGraph_CompactOmitsPropertyBag_FullKeepsIt()
    {
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            using (var store = CbmStore.OpenPath(CbmCachePaths.GetProjectDatabasePath(Project)))
            {
                store.UpsertProject(Project, "/tmp/verbosity-search");
                store.UpsertNode(new CbmNode
                {
                    Project = Project,
                    Label = "Method",
                    Name = "Execute",
                    QualifiedName = "Sample.Worker.Execute",
                    FilePath = "Worker.cs",
                    StartLine = 5,
                    EndLine = 8,
                    PropertiesJson = PropertyBagJson,
                });
            }

            var search = new SearchGraphService().Search(Project, namePattern: "Execute", limit: 5);
            var full = CbmMcpJson.FormatSearchGraph(Project, search, verbosity: CbmVerbosity.Full);
            var compact = CbmMcpJson.FormatSearchGraph(Project, search, verbosity: CbmVerbosity.Compact);
            var defaulted = CbmMcpJson.FormatSearchGraph(Project, search);

            Assert.Equal(full, defaulted);
            Assert.Contains("\"complexity\"", full, StringComparison.Ordinal);
            Assert.Contains("\"linear_scan_in_loop\"", full, StringComparison.Ordinal);
            Assert.DoesNotContain("\"complexity\"", compact, StringComparison.Ordinal);
            Assert.DoesNotContain("\"linear_scan_in_loop\"", compact, StringComparison.Ordinal);
            Assert.Contains("\"qualified_name\"", compact, StringComparison.Ordinal);
            Assert.Contains("\"in_degree\"", compact, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public void CodeSnippet_CompactOmitsPropertyBag_FullKeepsIt()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject("p", "/tmp/p");
        var node = new CbmNode
        {
            Project = "p",
            Label = "Method",
            Name = "Execute",
            QualifiedName = "Sample.Worker.Execute",
            FilePath = "Worker.cs",
            StartLine = 5,
            EndLine = 8,
            PropertiesJson = PropertyBagJson,
        };
        store.UpsertNode(node);
        var stored = store.FindNodeByQualifiedName("p", node.QualifiedName)
            ?? throw new InvalidOperationException("node missing after upsert");

        var snippet = new CbmCodeSnippetResult(
            Found: true,
            QualifiedName: stored.QualifiedName,
            FilePath: stored.FilePath,
            StartLine: stored.StartLine,
            EndLine: stored.EndLine,
            Code: "public string Execute() { return \"ok\"; }",
            MatchType: "exact",
            Suggestions: null,
            Error: null);

        var full = CbmMcpJson.FormatCodeSnippet(snippet, stored, false, store, CbmVerbosity.Full);
        var compact = CbmMcpJson.FormatCodeSnippet(snippet, stored, false, store, CbmVerbosity.Compact);
        var defaulted = CbmMcpJson.FormatCodeSnippet(snippet, stored, false, store);

        Assert.Equal(full, defaulted);
        Assert.Contains("\"complexity\"", full, StringComparison.Ordinal);
        Assert.DoesNotContain("\"complexity\"", compact, StringComparison.Ordinal);
        Assert.Contains("\"source\"", compact, StringComparison.Ordinal);
        Assert.Contains("\"callers\"", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_CompactCapsNestedLists_AndDropsScopedTotals()
    {
        var topNodes = Enumerable.Range(1, 12).Select(index => $"Node{index}").ToArray();
        var fileTree = Enumerable.Range(1, 12)
            .Select(index => new CbmFileTreeEntry($"src/f{index}.cs", "file", 0))
            .ToArray();
        var result = new CbmArchitectureResult(
            Project: "p",
            Path: "src",
            TotalNodes: 10,
            TotalEdges: 20,
            RootTotalNodes: 100,
            RootTotalEdges: 200,
            Structure: null,
            Dependencies: null,
            Languages: null,
            Packages: null,
            EntryPoints: null,
            Hotspots: null,
            Boundaries: null,
            Layers: null,
            Clusters:
            [
                new CbmClusterInfo(
                    Id: 1,
                    Label: "core",
                    Members: 12,
                    Cohesion: 0.5,
                    TopNodes: topNodes,
                    Packages: topNodes,
                    EdgeTypes: ["CALLS", "DEFINES", "USAGE", "WRITES", "IMPORTS", "EXTENDS", "IMPLEMENTS", "THROWS", "CASTS", "OTHER"]),
            ],
            FileTree: fileTree);

        var full = CbmMcpJson.FormatArchitecture(result, CbmVerbosity.Full);
        var compact = CbmMcpJson.FormatArchitecture(result, CbmVerbosity.Compact);
        var defaulted = CbmMcpJson.FormatArchitecture(result);

        Assert.Equal(full, defaulted);
        Assert.DoesNotContain("scoped_total_nodes", full, StringComparison.Ordinal);
        Assert.DoesNotContain("scoped_total_edges", compact, StringComparison.Ordinal);
        Assert.Contains("\"root_total_nodes\"", full, StringComparison.Ordinal);

        using var fullDocument = JsonDocument.Parse(full);
        using var compactDocument = JsonDocument.Parse(compact);
        Assert.Equal(12, fullDocument.RootElement.GetProperty("clusters")[0].GetProperty("top_nodes").GetArrayLength());
        Assert.Equal(8, compactDocument.RootElement.GetProperty("clusters")[0].GetProperty("top_nodes").GetArrayLength());
        Assert.Equal(12, fullDocument.RootElement.GetProperty("file_tree").GetArrayLength());
        Assert.Equal(8, compactDocument.RootElement.GetProperty("file_tree").GetArrayLength());
    }

    [Fact]
    public void SearchCode_CompactOmitsRawMatchesAndRedundantCounters()
    {
        var result = new CbmSearchCodeResult(
            Results:
            [
                new CbmSearchCodeHit(
                    1,
                    "Execute",
                    "Sample.Worker.Execute",
                    "Method",
                    "Worker.cs",
                    5,
                    8,
                    0,
                    1,
                    10,
                    [6]),
            ],
            RawMatches: [new CbmGrepMatch("other.cs", 2, "unmatched Execute")],
            Files: null,
            Directories: new Dictionary<string, int> { ["src"] = 1 },
            TotalGrepMatches: 2,
            TotalResults: 1,
            RawMatchCount: 1,
            ElapsedMs: 4,
            DedupRatio: "2.0x",
            Warnings: []);

        var full = CbmMcpJson.FormatSearchCode(result, CbmVerbosity.Full);
        var compact = CbmMcpJson.FormatSearchCode(result, CbmVerbosity.Compact);
        var defaulted = CbmMcpJson.FormatSearchCode(result);

        Assert.Equal(full, defaulted);
        using var fullDocument = JsonDocument.Parse(full);
        using var compactDocument = JsonDocument.Parse(compact);
        Assert.True(fullDocument.RootElement.TryGetProperty("raw_matches", out _));
        Assert.True(fullDocument.RootElement.TryGetProperty("total_grep_matches", out _));
        Assert.True(fullDocument.RootElement.TryGetProperty("dedup_ratio", out _));
        Assert.False(compactDocument.RootElement.TryGetProperty("raw_matches", out _));
        Assert.False(compactDocument.RootElement.TryGetProperty("total_grep_matches", out _));
        Assert.False(compactDocument.RootElement.TryGetProperty("dedup_ratio", out _));
        Assert.Equal(1, compactDocument.RootElement.GetProperty("raw_match_count").GetInt32());
        Assert.Equal(1, compactDocument.RootElement.GetProperty("total_results").GetInt32());
    }

    [Fact]
    public void FormatError_SerializesHintWithSnakeCaseOptions()
    {
        using var document = JsonDocument.Parse(
            CbmMcpJson.FormatError("project not found or not indexed", "Call index_repository first."));

        Assert.Equal("project not found or not indexed", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("Call index_repository first.", document.RootElement.GetProperty("hint").GetString());
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
                "cbm-verbosity-tests",
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
