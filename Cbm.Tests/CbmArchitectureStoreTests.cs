using System.Text.Json;
using Cbm.Graph;
using Cbm.Mcp;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmArchitectureStoreTests
{
    private const string Project = "test";

    [Fact]
    public void GetArchitecture_AllAspects_ReturnsSections()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project);

        Assert.True(result.Languages!.Count > 0);
        Assert.True(result.Packages!.Count > 0);
        Assert.True(result.EntryPoints!.Count > 0);
        Assert.True(result.Hotspots!.Count > 0);
        Assert.True(result.Boundaries!.Count > 0);
        Assert.NotNull(result.Structure);
        Assert.NotNull(result.Dependencies);
    }

    [Fact]
    public void GetArchitecture_SpecificAspects_FiltersOthers()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project, aspects: ["languages", "hotspots"]);

        Assert.True(result.Languages!.Count > 0);
        Assert.True(result.Hotspots!.Count > 0);
        Assert.Null(result.Packages);
        Assert.Null(result.EntryPoints);
        Assert.Null(result.Boundaries);
    }

    [Fact]
    public void Hotspots_ExcludeTestMethods()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project, aspects: ["hotspots"]);

        foreach (var hotspot in result.Hotspots!)
        {
            Assert.DoesNotContain("Test", hotspot.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EntryPoints_ExcludeTestFiles()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project, aspects: ["entry_points"]);

        Assert.Equal(2, result.EntryPoints!.Count);
        foreach (var entryPoint in result.EntryPoints)
        {
            Assert.DoesNotContain("test", entryPoint.File, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Boundaries_DetectsCrossPackageCalls()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project, aspects: ["boundaries"]);

        Assert.True(result.Boundaries!.Count > 0);
        Assert.Contains(result.Boundaries, boundary =>
            boundary.From.Length > 0 && boundary.To.Length > 0 && boundary.CallCount > 0);
    }

    [Fact]
    public void PathScoping_LimitsCounts()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject("pscope", "/tmp/pscope");

        store.UpsertNode(FileNode("pscope", "a.cs", "apps/foo/a.cs"));
        store.UpsertNode(FileNode("pscope", "b.cs", "other/b.cs"));
        store.UpsertNode(MethodNode("pscope", "Foo", "pscope.apps.foo.Foo", "apps/foo/a.cs"));
        store.UpsertNode(MethodNode("pscope", "Bar", "pscope.other.Bar", "other/b.cs"));

        var whole = store.GetArchitecture("pscope", aspects: ["structure", "dependencies"]);
        var scoped = store.GetArchitecture("pscope", "apps/foo", aspects: ["structure", "dependencies"]);

        Assert.True(whole.TotalNodes > scoped.TotalNodes);
        Assert.NotNull(scoped.Path);
        Assert.Equal("apps/foo", scoped.Path);
        Assert.NotNull(scoped.RootTotalNodes);
        Assert.True(scoped.RootTotalNodes > scoped.TotalNodes);
    }

    [Fact]
    public void Clusters_ReturnsNonSingletonCommunities()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");

        var ids = new long[8];
        for (var i = 0; i < 8; i++)
        {
            var group = i / 4;
            ids[i] = store.UpsertNode(MethodNode(
                Project,
                $"fn{i}",
                $"test.pkg{group}.mod.fn{i}",
                "src/work.cs"));
        }

        for (var g = 0; g < 2; g++)
        {
            for (var a = 0; a < 4; a++)
            {
                for (var b = a + 1; b < 4; b++)
                {
                    store.UpsertEdge(CallsEdge(ids[(g * 4) + a], ids[(g * 4) + b]));
                }
            }
        }

        store.UpsertEdge(CallsEdge(ids[0], ids[4]));

        var result = store.GetArchitecture(Project, aspects: ["clusters"]);

        Assert.True(result.Clusters!.Count >= 2);
        foreach (var cluster in result.Clusters)
        {
            Assert.True(cluster.Members >= 2);
            Assert.InRange(cluster.Cohesion, 0.0, 1.0);
            Assert.Single(cluster.EdgeTypes);
            Assert.Equal("CALLS", cluster.EdgeTypes[0]);
            Assert.NotEmpty(cluster.TopNodes);
        }
    }

    [Fact]
    public void Leiden_BasicTwoCommunities()
    {
        var nodeIds = new long[] { 1, 2, 3, 4, 5 };
        var edges = new (long Src, long Dst)[]
        {
            (1, 2), (2, 3), (1, 3), (4, 5),
        };

        var communities = LeidenClustering.DetectCommunities(nodeIds, edges);

        Assert.Equal(5, communities.Length);
        Assert.Equal(communities[0], communities[1]);
        Assert.Equal(communities[1], communities[2]);
        Assert.Equal(communities[3], communities[4]);
        Assert.NotEqual(communities[0], communities[3]);
    }

    [Fact]
    public void QnToPackage_ExtractsExpectedSegments()
    {
        Assert.Equal("store", CbmQualifiedNameHelpers.QnToPackage("project.internal.store.search.Search"));
        Assert.Equal("main", CbmQualifiedNameHelpers.QnToPackage("project.main.foo"));
        Assert.Equal(string.Empty, CbmQualifiedNameHelpers.QnToPackage("standalone"));
    }

    [Fact]
    public void RebuildSchemaProperties_FoldsTrueAndFalseToBoolean()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");
        store.UpsertNode(MethodNode(Project, "A", "test.A", "a.cs", """{"flag":true}"""));
        store.UpsertNode(MethodNode(Project, "B", "test.B", "b.cs", """{"flag":false}"""));

        store.RebuildSchemaProperties(Project);

        var flag = store.GetSchemaProperties(Project).Single(row => row.PropertyKey == "flag");
        Assert.Equal("boolean", flag.ValueType);
    }

    [Fact]
    public void RebuildSchemaProperties_StoresMixedWhenTypesDiffer()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");
        store.UpsertNode(MethodNode(Project, "A", "test.A", "a.cs", """{"x":1}"""));
        store.UpsertNode(MethodNode(Project, "B", "test.B", "b.cs", """{"x":"text"}"""));
        store.UpsertNode(MethodNode(Project, "C", "test.C", "c.cs", """{"y":true}"""));
        store.UpsertNode(MethodNode(Project, "D", "test.D", "d.cs", """{"y":1}"""));

        store.RebuildSchemaProperties(Project);

        Assert.Equal("mixed", store.GetSchemaProperties(Project).Single(row => row.PropertyKey == "x").ValueType);
        Assert.Equal("mixed", store.GetSchemaProperties(Project).Single(row => row.PropertyKey == "y").ValueType);
    }

    [Fact]
    public void FormatGraphSchema_UsesPropertiesSameAsForIdenticalLabels()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");
        const string shared = """{"score":1,"enabled":true,"name":"n"}""";
        store.UpsertNode(MethodNode(Project, "M1", "test.M1", "m.cs", shared));
        store.UpsertNode(MethodNode(Project, "M2", "test.M2", "m.cs", shared));
        store.UpsertNode(LabelledNode(Project, "Constructor", "C", "test.C", "c.cs", shared));
        store.RebuildSchemaProperties(Project);

        var response = new CbmGraphSchemaResponse(
            store.GetSchemaCounts(Project),
            store.GetSchemaProperties(Project));
        using var document = JsonDocument.Parse(CbmMcpJson.FormatGraphSchema(response));
        var labels = document.RootElement.GetProperty("node_labels").EnumerateArray().ToArray();
        var method = labels.Single(label => label.GetProperty("label").GetString() == "Method");
        var constructor = labels.Single(label => label.GetProperty("label").GetString() == "Constructor");
        Assert.True(method.TryGetProperty("properties", out _));
        Assert.False(method.TryGetProperty("properties_same_as", out _));
        Assert.Equal("Method", constructor.GetProperty("properties_same_as").GetString());
        Assert.False(constructor.TryGetProperty("properties", out _));
    }

    [Fact]
    public void FormatGraphSchema_DoesNotCollapseCallsOntoDefaultEdges()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");
        var sourceId = store.UpsertNode(MethodNode(Project, "Src", "test.Src", "src.cs"));
        var targetId = store.UpsertNode(MethodNode(Project, "Dst", "test.Dst", "dst.cs"));
        store.UpsertEdge(TypedEdge(
            sourceId,
            targetId,
            "USAGE",
            """{"confidence":1.0,"strategy":"roslyn"}"""));
        store.UpsertEdge(TypedEdge(
            sourceId,
            targetId,
            "CALLS",
            """{"confidence":1.0,"strategy":"roslyn","arg_count":0,"loop_depth":0,"branch_depth":0}"""));
        store.UpsertEdge(TypedEdge(sourceId, targetId, "DEFINES", "{}"));
        store.RebuildSchemaProperties(Project);

        var response = new CbmGraphSchemaResponse(
            store.GetSchemaCounts(Project),
            store.GetSchemaProperties(Project));
        using var document = JsonDocument.Parse(CbmMcpJson.FormatGraphSchema(response));
        var edges = document.RootElement.GetProperty("edge_types").EnumerateArray().ToArray();
        var calls = edges.Single(edge => edge.GetProperty("type").GetString() == "CALLS");
        var usage = edges.Single(edge => edge.GetProperty("type").GetString() == "USAGE");
        var defines = edges.Single(edge => edge.GetProperty("type").GetString() == "DEFINES");
        Assert.True(calls.TryGetProperty("properties", out var callProperties));
        Assert.Contains("arg_count", callProperties.GetProperty("integer").EnumerateArray().Select(value => value.GetString()));
        Assert.False(calls.TryGetProperty("properties_same_as", out _));
        Assert.True(usage.TryGetProperty("properties", out _));
        Assert.False(usage.TryGetProperty("properties_same_as", out _));
        Assert.False(defines.TryGetProperty("properties", out _));
        Assert.False(defines.TryGetProperty("properties_same_as", out _));
    }

    [Fact]
    public void FormatGraphSchema_FlagOff_EmitsEmptyPropertyArrays()
    {
        using var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");
        store.UpsertNode(MethodNode(Project, "M", "test.M", "m.cs", """{"n":1}"""));
        store.RebuildSchemaProperties(Project);
        Assert.NotEmpty(store.GetSchemaProperties(Project));

        var response = new CbmGraphSchemaResponse(
            store.GetSchemaCounts(Project),
            store.GetSchemaProperties(Project));

        foreach (var flag in new[] { "0", "false" })
        {
            Environment.SetEnvironmentVariable("CBM_SCHEMA_PROPERTIES", flag);
            try
            {
                using var document = JsonDocument.Parse(CbmMcpJson.FormatGraphSchema(response));
                Assert.False(document.RootElement.TryGetProperty("columns", out _));
                var label = document.RootElement.GetProperty("node_labels")[0];
                Assert.Equal(JsonValueKind.Array, label.GetProperty("properties").ValueKind);
                Assert.Equal(0, label.GetProperty("properties").GetArrayLength());
            }
            finally
            {
                Environment.SetEnvironmentVariable("CBM_SCHEMA_PROPERTIES", null);
            }
        }
    }

    [Fact]
    public void FormatArchitecture_OmitsPropertiesOnLabelsAndEdges()
    {
        using var store = CreateArchitectureStore();
        var result = store.GetArchitecture(Project, aspects: ["structure", "dependencies"]);
        using var document = JsonDocument.Parse(CbmMcpJson.FormatArchitecture(result));

        foreach (var label in document.RootElement.GetProperty("node_labels").EnumerateArray())
        {
            Assert.False(label.TryGetProperty("properties", out _));
        }

        foreach (var edge in document.RootElement.GetProperty("edge_types").EnumerateArray())
        {
            Assert.False(edge.TryGetProperty("properties", out _));
        }
    }

    private static CbmStore CreateArchitectureStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/test");

        foreach (var file in new[] { "main.cs", "handler.cs", "service.cs", "utils.cs" })
        {
            store.UpsertNode(FileNode(Project, file, file));
        }

        store.UpsertNode(NamespaceNode(Project, "Sample", "Sample.cs"));
        store.UpsertNode(NamespaceNode(Project, "Sample.Internal", "internal/handler.cs"));

        var mainId = store.UpsertNode(MethodNode(
            Project,
            "Main",
            "test.cmd.server.Main",
            "cmd/server/main.cs",
            """{"is_entry_point":true}"""));
        var handleId = store.UpsertNode(MethodNode(
            Project,
            "HandleRequest",
            "test.internal.handler.HandleRequest",
            "internal/handler/handler.cs",
            """{"is_entry_point":true}"""));
        var processId = store.UpsertNode(MethodNode(
            Project,
            "Process",
            "test.internal.service.Process",
            "internal/service/service.cs"));
        var helperId = store.UpsertNode(MethodNode(
            Project,
            "Helper",
            "test.internal.service.Helper",
            "internal/service/service.cs"));
        var testId = store.UpsertNode(MethodNode(
            Project,
            "TestHelper",
            "test.internal.service.TestHelper",
            "internal/service/service_test.cs",
            """{"is_test":true}"""));

        store.UpsertEdge(CallsEdge(mainId, handleId));
        store.UpsertEdge(CallsEdge(handleId, processId));
        store.UpsertEdge(CallsEdge(processId, helperId));
        store.UpsertEdge(CallsEdge(processId, helperId));
        store.UpsertEdge(CallsEdge(processId, testId));

        return store;
    }

    private static CbmNode FileNode(string project, string name, string filePath)
    {
        return new CbmNode
        {
            Project = project,
            Label = "File",
            Name = name,
            QualifiedName = $"{project}.{name}",
            FilePath = filePath,
            StartLine = 1,
            EndLine = 1,
        };
    }

    private static CbmNode NamespaceNode(string project, string name, string filePath)
    {
        return new CbmNode
        {
            Project = project,
            Label = "Namespace",
            Name = name,
            QualifiedName = $"{project}.{name}",
            FilePath = filePath,
            StartLine = 1,
            EndLine = 1,
        };
    }

    private static CbmNode MethodNode(
        string project,
        string name,
        string qualifiedName,
        string filePath,
        string propertiesJson = "{}")
    {
        return new CbmNode
        {
            Project = project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 3,
            PropertiesJson = propertiesJson,
        };
    }

    private static CbmEdge CallsEdge(long sourceId, long targetId)
    {
        return new CbmEdge
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = "CALLS",
        };
    }

    private static CbmNode LabelledNode(
        string project,
        string label,
        string name,
        string qualifiedName,
        string filePath,
        string propertiesJson)
    {
        return new CbmNode
        {
            Project = project,
            Label = label,
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 3,
            PropertiesJson = propertiesJson,
        };
    }

    private static CbmEdge TypedEdge(long sourceId, long targetId, string type, string propertiesJson)
    {
        return new CbmEdge
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
            PropertiesJson = propertiesJson,
        };
    }
}
