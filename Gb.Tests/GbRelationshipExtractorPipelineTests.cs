using Gb.Pipeline;
using Gb.Store;

namespace Gb.Tests;

[Collection("GbCache")]
[Trait("Category", "Integration")]
public sealed class GbRelationshipExtractorPipelineTests
{
    [Fact]
    public async Task PipelineIndexExposesCallEdgesInSchema()
    {
        using var scope = GbTestCacheScope.Create();
        using var repo = GbTestTempDirectory.Create("gb-relationship-pipeline");
        GbTestSampleProjects.WriteCallerCallee(repo.Path);
        scope.SetCacheDirectory();

        var indexResult = await new IndexRepository().IndexAsync(repo.Path);
        var schema = new GraphSchemaService().GetSchema(indexResult.ProjectName);
        Assert.Contains(schema.EdgeTypes, edge => edge.Type == "CALLS" && edge.Count > 0);

        using var store = GbStore.OpenPath(indexResult.DatabasePath);
        var target = store.SearchNodes(indexResult.ProjectName, namePattern: "Target", limit: 5)
            .Single(node => node.Name == "Target");
        var neighbors = store.GetNodeNeighborNames(target.Id, limit: 10);
        Assert.Contains("Run", neighbors.Callers);
    }
}
