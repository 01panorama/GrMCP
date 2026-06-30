using System.Text.Json;
using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmRuntimeTraceStoreTests
{
    private const string Project = "runtime-trace-store";

    [Fact]
    public void TraceObservationUpsert_AggregatesCountAvgP99AndErrors()
    {
        using var store = CreateStore();
        var entry = new CbmNormalizedTraceEntry(
            Caller: "Run",
            Callee: "Target",
            Service: "api",
            TargetService: string.Empty,
            Route: string.Empty,
            Method: string.Empty,
            StatusCode: "500",
            DurationMs: 100,
            Count: 2,
            Timestamp: "2026-01-01T00:00:00Z",
            AttributesJson: "{}");

        store.TraceObservationUpsert(Project, entry, null, null, null);

        var followUp = entry with
        {
            StatusCode = "200",
            DurationMs = 200,
            Count = 1,
            Timestamp = "2026-01-02T00:00:00Z",
        };
        store.TraceObservationUpsert(Project, followUp, null, null, null);

        var observations = store.ListRuntimeObservations(Project, limit: 1);
        Assert.Single(observations);
        var observation = observations[0];
        Assert.Equal(3, observation.Count);
        Assert.Equal(2, observation.ErrorCount);
        Assert.Equal(133.33333333333334, observation.AvgDurationMs, precision: 5);
        Assert.True(observation.P99DurationMs >= 100);
    }

    [Fact]
    public void FindCallsEdge_ReturnsStaticCallsEdgeId()
    {
        using var store = CreateStore();
        var callerId = store.UpsertNode(MethodNode("Run", "runtime.Run", "Caller.cs", 1, 5));
        var calleeId = store.UpsertNode(MethodNode("Target", "runtime.Target", "Callee.cs", 1, 5));
        var edgeId = store.UpsertEdge(Edge(callerId, calleeId, "CALLS"));

        var found = store.FindCallsEdge(Project, callerId, calleeId);

        Assert.Equal(edgeId, found);
    }

    [Fact]
    public void CountRuntimeObservations_TracksMatchedEdges()
    {
        using var store = CreateStore();
        var callerId = store.UpsertNode(MethodNode("Run", "runtime.Run", "Caller.cs", 1, 5));
        var calleeId = store.UpsertNode(MethodNode("Target", "runtime.Target", "Callee.cs", 1, 5));
        var edgeId = store.UpsertEdge(Edge(callerId, calleeId, "CALLS"));

        store.TraceObservationUpsert(
            Project,
            new CbmNormalizedTraceEntry(
                Caller: "Run",
                Callee: "Target",
                Service: string.Empty,
                TargetService: string.Empty,
                Route: string.Empty,
                Method: string.Empty,
                StatusCode: null,
                DurationMs: 10,
                Count: 1,
                Timestamp: null,
                AttributesJson: "{}"),
            callerId,
            calleeId,
            edgeId);

        Assert.Equal(1, store.CountRuntimeObservations(Project));
        Assert.Equal(1, store.CountMatchedRuntimeEdges(Project));
    }

    [Fact]
    public void DeleteProject_CascadesTraceObservations()
    {
        using var store = CreateStore();
        store.TraceObservationUpsert(
            Project,
            new CbmNormalizedTraceEntry(
                Caller: "A",
                Callee: "B",
                Service: string.Empty,
                TargetService: string.Empty,
                Route: "/health",
                Method: "GET",
                StatusCode: null,
                DurationMs: 5,
                Count: 1,
                Timestamp: null,
                AttributesJson: "{}"),
            null,
            null,
            null);

        store.DeleteProject(Project);

        Assert.Equal(0, store.CountRuntimeObservations(Project));
    }

    [Theory]
    [InlineData(new long[] { 10, 20, 30, 40, 100 }, 100)]
    [InlineData(new long[] { 5 }, 5)]
    public void CalculateP99_ReturnsExpectedPercentile(long[] values, long expected)
    {
        Assert.Equal(expected, CbmTraceDuration.CalculateP99(values));
    }

    private static CbmStore CreateStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/tmp/runtime-trace-store");
        return store;
    }

    private static CbmNode MethodNode(
        string name,
        string qualifiedName,
        string filePath,
        int startLine,
        int endLine)
    {
        return new CbmNode
        {
            Project = Project,
            Label = "Method",
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
        };
    }

    private static CbmEdge Edge(long sourceId, long targetId, string type)
    {
        return new CbmEdge
        {
            Project = Project,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
        };
    }
}
