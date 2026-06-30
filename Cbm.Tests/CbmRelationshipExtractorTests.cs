using System.Text.Json;
using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Roslyn;
using Cbm.Store;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmRelationshipExtractorTests
{
    private const string Project = "test-project";

    [Fact]
    public async Task ExtractsCrossFileCallEdge()
    {
        using var temp = TempDirectory.Create();
        WriteProject(temp.Path);
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
                public Callee() { }

                public string Target()
                {
                    return "ok";
                }
            }
            """);

        var edges = await ExtractRelationshipsAsync(temp.Path);

        Assert.Contains(
            edges,
            edge => edge.Type == "CALLS" &&
                edge.SourceQualifiedName.EndsWith("Sample.Caller.Run()") &&
                edge.TargetQualifiedName.EndsWith("Sample.Callee.Target()"));
        Assert.Contains(
            edges,
            edge => edge.Type == "CALLS" &&
                edge.SourceQualifiedName.EndsWith("Sample.Caller.Run()") &&
                edge.TargetQualifiedName.Contains("Sample.Callee.Callee("));
    }

    [Fact]
    public void ExtractsInheritanceInterfaceAndOverrideEdges()
    {
        using var temp = TempDirectory.Create();
        var edges = ExtractLooseRelationships(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Types.cs"] =
                    """
                    namespace Sample;

                    public interface IWorker
                    {
                        int Work();
                    }

                    public abstract class BaseWorker
                    {
                        public virtual int BaseMethod() => 1;
                    }

                    public sealed class Worker : BaseWorker, IWorker
                    {
                        public override int BaseMethod() => 2;

                        public int Work() => BaseMethod();
                    }
                    """,
            });

        Assert.Contains(
            edges,
            edge => edge.Type == "INHERITS" &&
                edge.SourceQualifiedName.EndsWith("Sample.Worker") &&
                edge.TargetQualifiedName.EndsWith("Sample.BaseWorker"));
        Assert.Contains(
            edges,
            edge => edge.Type == "IMPLEMENTS" &&
                edge.SourceQualifiedName.EndsWith("Sample.Worker") &&
                edge.TargetQualifiedName.EndsWith("Sample.IWorker"));
        Assert.Contains(
            edges,
            edge => edge.Type == "OVERRIDES" &&
                edge.SourceQualifiedName.EndsWith("Sample.Worker.BaseMethod()") &&
                edge.TargetQualifiedName.EndsWith("Sample.BaseWorker.BaseMethod()"));
        Assert.Contains(
            edges,
            edge => edge.Type == "CALLS" &&
                edge.SourceQualifiedName.EndsWith("Sample.Worker.Work()") &&
                edge.TargetQualifiedName.EndsWith("Sample.Worker.BaseMethod()"));
    }

    [Fact]
    public void ExtractsImportUsageAndWriteEdges()
    {
        using var temp = TempDirectory.Create();
        var edges = ExtractLooseRelationships(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Models.cs"] =
                    """
                    namespace Sample.Models;

                    public sealed class Counter
                    {
                        public int Value;
                    }
                    """,
                ["Worker.cs"] =
                    """
                    using Sample.Models;

                    namespace Sample;

                    public sealed class Worker
                    {
                        private readonly Counter counter = new();

                        public int Execute()
                        {
                            var current = counter.Value;
                            counter.Value = current + 1;
                            return current;
                        }
                    }
                    """,
            });

        Assert.Contains(
            edges,
            edge => edge.Type == "IMPORTS" &&
                edge.SourceQualifiedName.EndsWith("Worker") &&
                edge.TargetQualifiedName.EndsWith("Sample.Models"));
        Assert.Contains(
            edges,
            edge => edge.Type == "USAGE" &&
                edge.TargetQualifiedName.EndsWith("Sample.Models.Counter.Value"));
        Assert.Contains(
            edges,
            edge => edge.Type == "WRITES" &&
                edge.TargetQualifiedName.EndsWith("Sample.Models.Counter.Value"));
    }

    [Fact]
    public void StoreSmokePersistsRelationshipEdgesAndNeighbors()
    {
        using var temp = TempDirectory.Create();
        var definitions = ExtractLooseDefinitions(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Caller.cs"] =
                    """
                    namespace Sample;

                    public sealed class Caller
                    {
                        public string Run()
                        {
                            return new Callee().Target();
                        }
                    }
                    """,
                ["Callee.cs"] =
                    """
                    namespace Sample;

                    public sealed class Callee
                    {
                        public string Target() => "ok";
                    }
                    """,
            });
        var knownQualifiedNames = definitions.Nodes
            .Select(node => node.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        var relationships = new CSharpRelationshipExtractor().ExtractFromLooseDocuments(
            Project,
            temp.Path,
            LooseCSharpCompilation.CreateFromSources(
                new Dictionary<string, string>
                {
                    ["Caller.cs"] =
                        """
                        namespace Sample;

                        public sealed class Caller
                        {
                            public string Run()
                            {
                                return new Callee().Target();
                            }
                        }
                        """,
                    ["Callee.cs"] =
                        """
                        namespace Sample;

                        public sealed class Callee
                        {
                            public string Target() => "ok";
                        }
                        """,
                }).Documents,
            knownQualifiedNames);

        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, temp.Path);
        var idsByQualifiedName = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var node in definitions.Nodes)
        {
            idsByQualifiedName[node.QualifiedName] = store.UpsertNode(node);
        }

        store.UpsertGraphEdges(Project, idsByQualifiedName, relationships);

        var runMethod = definitions.Nodes.Single(node => node.Name == "Run");
        var targetMethod = definitions.Nodes.Single(node => node.Name == "Target");
        var runId = idsByQualifiedName[runMethod.QualifiedName];
        var targetId = idsByQualifiedName[targetMethod.QualifiedName];
        var runDegree = store.GetNodeDegree(runId);
        var targetDegree = store.GetNodeDegree(targetId);
        Assert.Equal(0, runDegree.InDegree);
        Assert.True(runDegree.OutDegree >= 1);
        Assert.Equal(1, targetDegree.InDegree);

        var neighbors = store.GetNodeNeighborNames(targetId, limit: 10);
        Assert.Contains("Run", neighbors.Callers);
    }

    [Fact]
    public void PropagatesTransitiveLoopDepthAlongCalls()
    {
        using var temp = TempDirectory.Create();
        var definitions = ExtractLooseDefinitions(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Loops.cs"] =
                    """
                    namespace Sample;

                    public sealed class Looping
                    {
                        public void Outer()
                        {
                            Inner();
                        }

                        public void Inner()
                        {
                            for (var i = 0; i < 3; i++)
                            {
                            }
                        }
                    }
                    """,
            });
        var knownQualifiedNames = definitions.Nodes
            .Select(node => node.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        var relationships = new CSharpRelationshipExtractor().ExtractFromLooseDocuments(
            Project,
            temp.Path,
            LooseCSharpCompilation.CreateFromSources(
                new Dictionary<string, string>
                {
                    ["Loops.cs"] =
                        """
                        namespace Sample;

                        public sealed class Looping
                        {
                            public void Outer()
                            {
                                Inner();
                            }

                            public void Inner()
                            {
                                for (var i = 0; i < 3; i++)
                                {
                                }
                            }
                        }
                        """,
                }).Documents,
            knownQualifiedNames);

        var mergedEdges = definitions.Edges.Concat(relationships).ToArray();
        var updatedNodes = CSharpTransitiveLoopDepth.Apply(definitions.Nodes, mergedEdges);

        var outer = updatedNodes.Single(node => node.Name == "Outer");
        var inner = updatedNodes.Single(node => node.Name == "Inner");
        var outerProps = ParseProperties(outer.PropertiesJson);
        var innerProps = ParseProperties(inner.PropertiesJson);

        Assert.Equal(1, GetInt(innerProps, "loop_depth"));
        Assert.Equal(1, GetInt(outerProps, "transitive_loop_depth"));
        Assert.Equal(1, GetInt(innerProps, "transitive_loop_depth"));
    }

    [Fact]
    public async Task PipelineIndexExposesCallEdgesInSchema()
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
            var schema = new GraphSchemaService().GetSchema(indexResult.ProjectName);
            Assert.Contains(schema.EdgeTypes, edge => edge.Type == "CALLS" && edge.Count > 0);

            using var store = CbmStore.OpenPath(indexResult.DatabasePath);
            var target = store.SearchNodes(indexResult.ProjectName, namePattern: "Target", limit: 5)
                .Single(node => node.Name == "Target");
            var neighbors = store.GetNodeNeighborNames(target.Id, limit: 10);
            Assert.Contains("Run", neighbors.Callers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static async Task<IReadOnlyList<CbmGraphEdge>> ExtractRelationshipsAsync(string repoRoot)
    {
        var loaded = await new CSharpProjectLoader()
            .LoadAsync(Path.Combine(repoRoot, "Sample.csproj"));
        var documents = loaded.Projects.Single().Documents;
        var definitions = new CSharpDefinitionExtractor()
            .ExtractFromLoadedDocuments(Project, repoRoot, documents);
        var knownQualifiedNames = definitions.Nodes
            .Select(node => node.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        return new CSharpRelationshipExtractor().ExtractFromLoadedDocuments(
            Project,
            repoRoot,
            documents,
            knownQualifiedNames);
    }

    private static IReadOnlyList<CbmGraphEdge> ExtractLooseRelationships(
        string repoRoot,
        IReadOnlyDictionary<string, string> sources)
    {
        var loose = LooseCSharpCompilation.CreateFromSources(sources);
        var definitions = new CSharpDefinitionExtractor()
            .ExtractFromLooseDocuments(Project, repoRoot, loose.Documents);
        var knownQualifiedNames = definitions.Nodes
            .Select(node => node.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        return new CSharpRelationshipExtractor().ExtractFromLooseDocuments(
            Project,
            repoRoot,
            loose.Documents,
            knownQualifiedNames);
    }

    private static CbmDefinitionExtractionResult ExtractLooseDefinitions(
        string repoRoot,
        IReadOnlyDictionary<string, string> sources)
    {
        var loose = LooseCSharpCompilation.CreateFromSources(sources);
        return new CSharpDefinitionExtractor().ExtractFromLooseDocuments(Project, repoRoot, loose.Documents);
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

    private static JsonElement ParseProperties(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    private static int GetInt(JsonElement properties, string name)
    {
        return properties.GetProperty(name).GetInt32();
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
                "cbm-relationship-tests",
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
