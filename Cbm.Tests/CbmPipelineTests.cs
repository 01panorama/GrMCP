using Cbm.Pipeline;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmPipelineTests
{
    [Fact]
    public void DerivesStableProjectNameFromRepositoryPath()
    {
        var projectName = CbmProjectNaming.DeriveFromPath("/Users/test/my project/GrMCP");

        Assert.Equal("Users-test-my-project-GrMCP", projectName);
        Assert.True(CbmProjectNaming.IsValidProjectName(projectName));
    }

    [Fact]
    public async Task IndexesTempRepositoryAndSupportsReadServices()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
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
            Assert.True(File.Exists(indexResult.DatabasePath));
            Assert.True(indexResult.NodeCount > 0);
            Assert.True(indexResult.EdgeCount > 0);

            var status = CbmProjectCatalog.GetIndexStatus(indexResult.ProjectName);
            Assert.Equal("ready", status.Status);
            Assert.Equal(temp.Path, Path.GetFullPath(status.RootPath));
            Assert.Equal(indexResult.NodeCount, status.Nodes);
            Assert.Equal(indexResult.EdgeCount, status.Edges);

            var projects = CbmProjectCatalog.ListProjects();
            Assert.Contains(projects, project => project.Name == indexResult.ProjectName);

            var schema = new GraphSchemaService().GetSchema(indexResult.ProjectName);
            Assert.Contains(schema.NodeLabels, label => label.Label == "Method" && label.Count > 0);
            Assert.Contains(schema.EdgeTypes, edge => edge.Type == "DEFINES_METHOD" && edge.Count > 0);

            var search = new SearchGraphService().Search(
                indexResult.ProjectName,
                namePattern: "Execute",
                limit: 5);
            Assert.True(search.Total >= 1);
            Assert.Contains(search.Results, node => node.Name == "Execute");
            Assert.False(search.HasMore);

            var methodQualifiedName = search.Results.Single(node => node.Name == "Execute").QualifiedName;
            var snippet = new CodeSnippetService().GetSnippet(indexResult.ProjectName, methodQualifiedName);
            Assert.True(snippet.Found);
            Assert.Equal("exact", snippet.MatchType);
            Assert.Contains("Execute", snippet.Code, StringComparison.Ordinal);

            var suffixSnippet = new CodeSnippetService().GetSnippet(indexResult.ProjectName, "Sample.Worker");
            Assert.True(suffixSnippet.Found);
            Assert.Equal("suffix", suffixSnippet.MatchType);

            var queryResult = new QueryGraphService().Query(
                indexResult.ProjectName,
                "MATCH (n:Method) WHERE n.name = 'Execute' RETURN n.name");
            Assert.Single(queryResult.Rows);
            Assert.Equal("Execute", queryResult.Rows[0][0]);

            var architecture = new GraphArchitectureService().GetArchitecture(indexResult.ProjectName);
            Assert.True(architecture.TotalNodes > 0);
            Assert.Contains(architecture.Structure!.NodeLabels, label => label.Label == "Method");
            Assert.NotNull(architecture.Languages);
            Assert.Single(architecture.Languages);
            Assert.Equal("C#", architecture.Languages[0].Language);
            Assert.Contains(architecture.Packages!, pkg => pkg.Name == "Sample");
            Assert.NotNull(architecture.Hotspots);

            Assert.True(CbmProjectCatalog.DeleteProject(indexResult.ProjectName));
            Assert.Equal("not_found", CbmProjectCatalog.GetIndexStatus(indexResult.ProjectName).Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task SearchPaginationReportsTotalAndHasMore()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
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
                "Types.cs",
                """
                namespace Sample;

                public sealed class Alpha { public int One() => 1; }
                public sealed class Beta { public int Two() => 2; }
                public sealed class Gamma { public int Three() => 3; }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var search = new SearchGraphService().Search(
                indexResult.ProjectName,
                label: "Method",
                limit: 1,
                offset: 0);

            Assert.Equal(3, search.Total);
            Assert.Single(search.Results);
            Assert.True(search.HasMore);

            var secondPage = new SearchGraphService().Search(
                indexResult.ProjectName,
                label: "Method",
                limit: 1,
                offset: 1);
            Assert.Equal(3, secondPage.Total);
            Assert.Single(secondPage.Results);
            Assert.NotEqual(search.Results[0].QualifiedName, secondPage.Results[0].QualifiedName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task SnippetLookupReturnsSuggestionsForAmbiguousSuffix()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
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
                "Types.cs",
                """
                namespace Sample.A;
                public sealed class Worker { public int Run() => 1; }
                namespace Sample.B;
                public sealed class Worker { public int Run() => 2; }
                """);

            var indexResult = await new IndexRepository().IndexAsync(temp.Path);
            var snippet = new CodeSnippetService().GetSnippet(indexResult.ProjectName, "Worker");
            Assert.False(snippet.Found);
            Assert.Equal("ambiguous", snippet.MatchType);
            Assert.NotNull(snippet.Suggestions);
            Assert.True(snippet.Suggestions!.Count >= 2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
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
                "cbm-pipeline-tests",
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
