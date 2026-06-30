using System.Globalization;
using System.Text.Json;
using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore
{
    private const int MaxDurationSamples = 100;

    public long? FindCallsEdge(string project, long callerNodeId, long calleeNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT id
            FROM edges
            WHERE project = $project
              AND type = 'CALLS'
              AND source_id = $callerId
              AND target_id = $calleeId
            LIMIT 1;
            """);
        Add(command, "$project", project);
        Add(command, "$callerId", callerNodeId);
        Add(command, "$calleeId", calleeNodeId);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public void TraceObservationUpsert(
        string project,
        CbmNormalizedTraceEntry entry,
        long? callerNodeId,
        long? calleeNodeId,
        long? callsEdgeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(entry);

        var existing = LoadTraceObservation(
            project,
            entry.Caller,
            entry.Callee,
            entry.Service,
            entry.TargetService,
            entry.Route,
            entry.Method);

        var incomingCount = Math.Max(1, entry.Count);
        var incomingErrors = CbmTraceSpanParser.IsErrorStatus(entry.StatusCode) ? incomingCount : 0;
        var incomingDurationMs = entry.DurationMs ?? 0;
        var lastSeen = string.IsNullOrWhiteSpace(entry.Timestamp)
            ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            : entry.Timestamp;

        var mergedCount = existing?.Count ?? 0;
        var mergedErrors = existing?.ErrorCount ?? 0;
        var mergedAvg = existing?.AvgDurationMs ?? 0;
        var samples = existing?.DurationSamples ?? [];

        mergedCount += incomingCount;
        mergedErrors += incomingErrors;

        if (incomingDurationMs > 0)
        {
            var sampleValue = (long)Math.Round(incomingDurationMs, MidpointRounding.AwayFromZero);
            samples = samples
                .Concat(Enumerable.Repeat(sampleValue, incomingCount))
                .TakeLast(MaxDurationSamples)
                .ToArray();
            var previousTotal = (existing?.AvgDurationMs ?? 0) * (existing?.Count ?? 0);
            var incomingTotal = incomingDurationMs * incomingCount;
            mergedAvg = mergedCount > 0 ? (previousTotal + incomingTotal) / mergedCount : 0;
        }
        else if (existing is not null)
        {
            mergedAvg = existing.AvgDurationMs;
        }

        var mergedP99 = samples.Count > 0 ? CbmTraceDuration.CalculateP99(samples) : 0;
        var resolvedCallerId = callerNodeId ?? existing?.CallerNodeId;
        var resolvedCalleeId = calleeNodeId ?? existing?.CalleeNodeId;
        var resolvedEdgeId = callsEdgeId ?? existing?.CallsEdgeId;

        using var command = CreateCommand(
            """
            INSERT INTO trace_observations (
              project, caller, callee, service, target_service, route, method,
              caller_node_id, callee_node_id, calls_edge_id,
              count, error_count, avg_duration_ms, p99_duration_ms, last_seen,
              duration_samples, attributes)
            VALUES (
              $project, $caller, $callee, $service, $targetService, $route, $method,
              $callerNodeId, $calleeNodeId, $callsEdgeId,
              $count, $errorCount, $avgDurationMs, $p99DurationMs, $lastSeen,
              $durationSamples, $attributes)
            ON CONFLICT(project, caller, callee, service, target_service, route, method)
            DO UPDATE SET
              caller_node_id = excluded.caller_node_id,
              callee_node_id = excluded.callee_node_id,
              calls_edge_id = excluded.calls_edge_id,
              count = excluded.count,
              error_count = excluded.error_count,
              avg_duration_ms = excluded.avg_duration_ms,
              p99_duration_ms = excluded.p99_duration_ms,
              last_seen = excluded.last_seen,
              duration_samples = excluded.duration_samples,
              attributes = excluded.attributes;
            """);
        Add(command, "$project", project);
        Add(command, "$caller", entry.Caller);
        Add(command, "$callee", entry.Callee);
        Add(command, "$service", entry.Service);
        Add(command, "$targetService", entry.TargetService);
        Add(command, "$route", entry.Route);
        Add(command, "$method", entry.Method);
        Add(command, "$callerNodeId", resolvedCallerId);
        Add(command, "$calleeNodeId", resolvedCalleeId);
        Add(command, "$callsEdgeId", resolvedEdgeId);
        Add(command, "$count", mergedCount);
        Add(command, "$errorCount", mergedErrors);
        Add(command, "$avgDurationMs", mergedAvg);
        Add(command, "$p99DurationMs", mergedP99);
        Add(command, "$lastSeen", lastSeen);
        Add(command, "$durationSamples", JsonSerializer.Serialize(samples));
        Add(command, "$attributes", entry.AttributesJson);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CbmRuntimeObservation> ListRuntimeObservations(string project, int limit = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT caller, callee, service, target_service, route, method,
                   count, error_count, avg_duration_ms, p99_duration_ms, calls_edge_id
            FROM trace_observations
            WHERE project = $project
            ORDER BY count DESC, avg_duration_ms DESC
            LIMIT $limit;
            """);
        Add(command, "$project", project);
        Add(command, "$limit", Math.Max(1, limit));

        var observations = new List<CbmRuntimeObservation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            observations.Add(new CbmRuntimeObservation(
                Caller: reader.GetString(0),
                Callee: reader.GetString(1),
                Service: reader.GetString(2),
                TargetService: reader.GetString(3),
                Route: reader.GetString(4),
                Method: reader.GetString(5),
                Count: reader.GetInt32(6),
                ErrorCount: reader.GetInt32(7),
                AvgDurationMs: reader.GetDouble(8),
                P99DurationMs: reader.GetDouble(9),
                Matched: !reader.IsDBNull(10)));
        }

        return observations;
    }

    public int CountRuntimeObservations(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            "SELECT COUNT(*) FROM trace_observations WHERE project = $project;");
        Add(command, "$project", project);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int CountMatchedRuntimeEdges(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT COUNT(*)
            FROM trace_observations
            WHERE project = $project AND calls_edge_id IS NOT NULL;
            """);
        Add(command, "$project", project);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private TraceObservationRow? LoadTraceObservation(
        string project,
        string caller,
        string callee,
        string service,
        string targetService,
        string route,
        string method)
    {
        using var command = CreateCommand(
            """
            SELECT count, error_count, avg_duration_ms, p99_duration_ms,
                   caller_node_id, callee_node_id, calls_edge_id, duration_samples
            FROM trace_observations
            WHERE project = $project
              AND caller = $caller
              AND callee = $callee
              AND service = $service
              AND target_service = $targetService
              AND route = $route
              AND method = $method
            LIMIT 1;
            """);
        Add(command, "$project", project);
        Add(command, "$caller", caller);
        Add(command, "$callee", callee);
        Add(command, "$service", service);
        Add(command, "$targetService", targetService);
        Add(command, "$route", route);
        Add(command, "$method", method);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var samplesJson = reader.GetString(7);
        var samples = string.IsNullOrWhiteSpace(samplesJson)
            ? Array.Empty<long>()
            : JsonSerializer.Deserialize<long[]>(samplesJson) ?? Array.Empty<long>();

        return new TraceObservationRow(
            Count: reader.GetInt32(0),
            ErrorCount: reader.GetInt32(1),
            AvgDurationMs: reader.GetDouble(2),
            P99DurationMs: reader.GetDouble(3),
            CallerNodeId: reader.IsDBNull(4) ? null : reader.GetInt64(4),
            CalleeNodeId: reader.IsDBNull(5) ? null : reader.GetInt64(5),
            CallsEdgeId: reader.IsDBNull(6) ? null : reader.GetInt64(6),
            DurationSamples: samples);
    }

    private sealed record TraceObservationRow(
        int Count,
        int ErrorCount,
        double AvgDurationMs,
        double P99DurationMs,
        long? CallerNodeId,
        long? CalleeNodeId,
        long? CallsEdgeId,
        IReadOnlyList<long> DurationSamples);
}
