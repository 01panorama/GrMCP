using System.Text.Json;
using Cbm.Graph;
using Cbm.Roslyn;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmDefinitionExtractorTests
{
    private const string Project = "test-project";

    [Fact]
    public async Task ExtractsCrossFileDefinitionsWithContainmentEdges()
    {
        using var temp = TempDirectory.Create();
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
                public string Target()
                {
                    return "ok";
                }
            }
            """);

        var loaded = await new CSharpProjectLoader()
            .LoadAsync(Path.Combine(temp.Path, "Sample.csproj"));
        var extraction = new CSharpDefinitionExtractor()
            .ExtractFromLoadedDocuments(Project, temp.Path, loaded.Projects.Single().Documents);

        Assert.Contains(extraction.Nodes, node => node.Label == "File" && node.Name == "Caller");
        Assert.Contains(extraction.Nodes, node => node.Label == "File" && node.Name == "Callee");
        Assert.Contains(extraction.Nodes, node => node.Label == "Namespace" && node.QualifiedName == $"{Project}.Sample");
        Assert.Contains(
            extraction.Nodes,
            node => node.Label == "Class" && node.QualifiedName == $"{Project}.Sample.Caller");
        Assert.Contains(
            extraction.Nodes,
            node => node.Label == "Class" && node.QualifiedName == $"{Project}.Sample.Callee");
        Assert.Contains(
            extraction.Nodes,
            node => node.Label == "Method" && node.QualifiedName.EndsWith("Sample.Caller.Run()"));
        Assert.Contains(
            extraction.Nodes,
            node => node.Label == "Method" && node.QualifiedName.EndsWith("Sample.Callee.Target()"));

        Assert.Contains(
            extraction.Edges,
            edge => edge.Type == "DEFINES" &&
                edge.SourceQualifiedName == $"{Project}.Sample" &&
                edge.TargetQualifiedName == $"{Project}.Sample.Caller");
        Assert.Contains(
            extraction.Edges,
            edge => edge.Type == "DEFINES_METHOD" &&
                edge.SourceQualifiedName == $"{Project}.Sample.Caller" &&
                edge.TargetQualifiedName == $"{Project}.Sample.Caller.Run()");
    }

    [Fact]
    public void ExtractsTypeSurfaceAndComplexityMetrics()
    {
        using var temp = TempDirectory.Create();
        var extraction = ExtractLoose(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Types.cs"] =
                    """
                    namespace Sample.Models;

                    public interface IWorker
                    {
                        string Work();
                    }

                    public enum Status
                    {
                        Active,
                        Inactive,
                    }

                    public delegate void Handler(string message);

                    public sealed class Worker
                    {
                        private readonly string field = "x";
                        public event Handler? Changed;
                        public string Name { get; set; }

                        public int Metrics(int n)
                        {
                            var sum = 0;
                            for (var i = 0; i < n; i++)
                            {
                                if (i % 2 == 0 && i % 3 == 0)
                                {
                                    sum += i;
                                }
                                else if (i % 2 == 0)
                                {
                                    sum += 1;
                                }
                            }

                            return sum;
                        }

                        public int Unguarded(int n) => n <= 0 ? 0 : Unguarded(n - 1);

                        public int Guarded(int n)
                        {
                            if (n <= 0)
                            {
                                return 0;
                            }
                            else
                            {
                                return Guarded(n - 1);
                            }
                        }

                        public int ScanInLoop(System.Collections.Generic.List<int> items)
                        {
                            var total = 0;
                            for (var i = 0; i < items.Count; i++)
                            {
                                if (items.Contains(i))
                                {
                                    total += items.IndexOf(i);
                                }

                                total += items.First(x => x > 0);
                                var copy = items.ToList();
                                total += copy.Count;
                            }

                            return total;
                        }

                        public int DeepAccess()
                        {
                            return new Root().Child.Grand.Child.Value;
                        }
                    }

                    internal sealed class Root
                    {
                        public Middle Child { get; } = new();
                    }

                    internal sealed class Middle
                    {
                        public Leaf Grand { get; } = new();
                    }

                    internal sealed class Leaf
                    {
                        public ValueHolder Child { get; } = new();
                    }

                    internal sealed class ValueHolder
                    {
                        public int Value => 7;
                    }
                    """,
            });

        Assert.Contains(extraction.Nodes, node => node.Label == "Interface");
        Assert.Contains(extraction.Nodes, node => node.Label == "Enum");
        Assert.Contains(extraction.Nodes, node => node.Label == "EnumMember" && node.Name == "Active");
        Assert.Contains(extraction.Nodes, node => node.Label == "Delegate");
        Assert.Contains(extraction.Nodes, node => node.Label == "Field");
        Assert.Contains(extraction.Nodes, node => node.Label == "Property");
        Assert.Contains(extraction.Nodes, node => node.Label == "Event");
        Assert.Contains(extraction.Nodes, node => node.Label == "Variable" && node.Name == "sum");

        var metricsNode = extraction.Nodes.Single(node => node.Name == "Metrics");
        var metrics = ParseProperties(metricsNode.PropertiesJson);
        Assert.Equal(5, GetInt(metrics, "complexity"));
        Assert.True(GetInt(metrics, "cognitive") >= 4);
        Assert.Equal(1, GetInt(metrics, "loop_count"));
        Assert.Equal(1, GetInt(metrics, "loop_depth"));
        Assert.Equal(1, GetInt(metrics, "param_count"));
        Assert.Equal("int", GetString(metrics, "return_type"));

        var unguarded = extraction.Nodes.Single(node => node.Name == "Unguarded");
        var unguardedProps = ParseProperties(unguarded.PropertiesJson);
        Assert.True(GetBool(unguardedProps, "self_recursive"));
        Assert.True(GetBool(unguardedProps, "unguarded_recursion"));

        var guarded = extraction.Nodes.Single(node => node.Name == "Guarded");
        var guardedProps = ParseProperties(guarded.PropertiesJson);
        Assert.True(GetBool(guardedProps, "self_recursive"));
        Assert.False(GetBool(guardedProps, "unguarded_recursion"));

        var scan = extraction.Nodes.Single(node => node.Name == "ScanInLoop");
        var scanProps = ParseProperties(scan.PropertiesJson);
        Assert.True(GetInt(scanProps, "linear_scan_in_loop") >= 2);
        Assert.True(GetInt(scanProps, "alloc_in_loop") >= 1);

        var deepAccess = extraction.Nodes.Single(node => node.Name == "DeepAccess");
        var deepProps = ParseProperties(deepAccess.PropertiesJson);
        Assert.True(GetInt(deepProps, "max_access_depth") >= 3);
    }

    [Fact]
    public void StoreSmokePersistsExtractedDefinitionsAndEdges()
    {
        using var temp = TempDirectory.Create();
        var extraction = ExtractLoose(
            temp.Path,
            new Dictionary<string, string>
            {
                ["Worker.cs"] =
                    """
                    namespace Sample;

                    public sealed class Worker
                    {
                        public int Execute(int value)
                        {
                            return value + 1;
                        }
                    }
                    """,
            });

        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, temp.Path);

        var idsByQualifiedName = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var node in extraction.Nodes)
        {
            idsByQualifiedName[node.QualifiedName] = store.UpsertNode(node);
        }

        foreach (var edge in extraction.Edges)
        {
            store.UpsertEdge(new CbmEdge
            {
                Project = Project,
                SourceId = idsByQualifiedName[edge.SourceQualifiedName],
                TargetId = idsByQualifiedName[edge.TargetQualifiedName],
                Type = edge.Type,
                PropertiesJson = edge.PropertiesJson,
            });
        }

        var method = store.FindNodeByQualifiedName(Project, extraction.Nodes.Single(n => n.Name == "Execute").QualifiedName);
        var classNode = store.FindNodesByQualifiedNameSuffix(Project, "Sample.Worker").Single();

        Assert.NotNull(method);
        Assert.Equal("Method", method!.Label);
        Assert.Equal("Class", classNode.Label);

        var methodDegree = store.BatchCountDegrees([method.Id], "DEFINES_METHOD");
        Assert.Equal(1, methodDegree[method.Id].InDegree);
        Assert.Equal(0, methodDegree[method.Id].OutDegree);
    }

    private static CbmDefinitionExtractionResult ExtractLoose(
        string repoRoot,
        IReadOnlyDictionary<string, string> sources)
    {
        var loose = LooseCSharpCompilation.CreateFromSources(sources);
        return new CSharpDefinitionExtractor().ExtractFromLooseDocuments(Project, repoRoot, loose.Documents);
    }

    private static JsonElement ParseProperties(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    private static int GetInt(JsonElement properties, string name)
    {
        return properties.GetProperty(name).GetInt32();
    }

    private static string GetString(JsonElement properties, string name)
    {
        return properties.GetProperty(name).GetString() ?? string.Empty;
    }

    private static bool GetBool(JsonElement properties, string name)
    {
        return properties.GetProperty(name).GetBoolean();
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
                "cbm-definition-tests",
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
