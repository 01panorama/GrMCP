using Gb.Graph;
using Gb.Mcp;
using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbMcpJsonVerbosityCacheTests
{
    private const string Project = "verbosity-search";
    private const string PropertyBagJson =
        """
        {"complexity":4,"cognitive":3,"linear_scan_in_loop":true,"signature":"()","return_type":"string","parent_class":"Worker"}
        """;

    [Fact]
    public void SearchGraph_CompactOmitsPropertyBag_FullKeepsIt()
    {
        using var scope = GbTestCacheScope.Create();
        scope.SetCacheDirectory();

        using (var store = GbStore.OpenPath(GbCachePaths.GetProjectDatabasePath(Project)))
        {
            store.UpsertProject(Project, "/tmp/verbosity-search");
            store.UpsertNode(new GbNode
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
        var full = GbMcpJson.FormatSearchGraph(Project, search, verbosity: GbVerbosity.Full);
        var compact = GbMcpJson.FormatSearchGraph(Project, search, verbosity: GbVerbosity.Compact);
        var defaulted = GbMcpJson.FormatSearchGraph(Project, search);

        Assert.Equal(full, defaulted);
        Assert.Contains("\"complexity\"", full, StringComparison.Ordinal);
        Assert.Contains("\"linear_scan_in_loop\"", full, StringComparison.Ordinal);
        Assert.DoesNotContain("\"complexity\"", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("\"linear_scan_in_loop\"", compact, StringComparison.Ordinal);
        Assert.Contains("\"qualified_name\"", compact, StringComparison.Ordinal);
        Assert.Contains("\"in_degree\"", compact, StringComparison.Ordinal);
    }
}
