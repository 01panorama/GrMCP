using System.Globalization;
using System.Text.Json;
using Cbm.Graph;

namespace Cbm.Store;

public static class CbmTraceSpanParser
{
    private static readonly string[] HttpMethodKeys = ["http.method", "http.request.method"];
    private static readonly string[] HttpRouteKeys = ["http.route", "http.target", "url.path"];
    private static readonly string[] HttpStatusKeys = ["http.status_code"];

    public static (CbmNormalizedTraceEntry? Entry, string? Warning) Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, "trace entry must be a JSON object");
        }

        var attributes = CollectAttributes(element);
        var resourceAttributes = element.TryGetProperty("resource", out var resource)
            ? CollectAttributes(resource)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in resourceAttributes)
        {
            attributes.TryAdd(key, value);
        }

        var caller = GetString(element, "caller")
            ?? GetString(element, "parent")
            ?? GetString(element, "parent_span_name")
            ?? string.Empty;
        var callee = GetString(element, "callee")
            ?? GetString(element, "name")
            ?? GetString(element, "span_name")
            ?? string.Empty;
        var service = GetString(element, "service")
            ?? attributes.GetValueOrDefault("service.name")
            ?? string.Empty;
        var targetService = GetString(element, "target_service") ?? string.Empty;
        var route = GetString(element, "route") ?? string.Empty;
        var method = GetString(element, "method") ?? string.Empty;
        var statusCode = GetString(element, "status_code");

        foreach (var key in HttpMethodKeys)
        {
            if (string.IsNullOrEmpty(method) && attributes.TryGetValue(key, out var httpMethod))
            {
                method = httpMethod;
            }
        }

        foreach (var key in HttpRouteKeys)
        {
            if (string.IsNullOrEmpty(route) && attributes.TryGetValue(key, out var httpRoute))
            {
                route = httpRoute;
            }
        }

        if (string.IsNullOrEmpty(route) && attributes.TryGetValue("url.full", out var fullUrl))
        {
            route = CbmTraceDuration.ExtractPathFromUrl(fullUrl);
        }

        if (string.IsNullOrEmpty(statusCode))
        {
            foreach (var key in HttpStatusKeys)
            {
                if (attributes.TryGetValue(key, out var httpStatus))
                {
                    statusCode = httpStatus;
                    break;
                }
            }
        }

        var durationMs = CbmTraceDuration.ParseDurationMs(
            element.TryGetProperty("duration_ms", out var durationMsElement) ? durationMsElement : null,
            element.TryGetProperty("duration_ns", out var durationNsElement) ? durationNsElement : null);

        if (durationMs is null
            && element.TryGetProperty("start_time", out var startTime)
            && element.TryGetProperty("end_time", out var endTime))
        {
            var durationNs = CbmTraceDuration.ParseDurationNs(
                startTime.ValueKind == JsonValueKind.String ? startTime.GetString() : startTime.GetRawText(),
                endTime.ValueKind == JsonValueKind.String ? endTime.GetString() : endTime.GetRawText());
            if (durationNs > 0)
            {
                durationMs = durationNs / 1_000_000.0;
            }
        }

        var count = 1;
        if (element.TryGetProperty("count", out var countElement))
        {
            if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out var parsedCount))
            {
                count = Math.Max(1, parsedCount);
            }
            else if (countElement.ValueKind == JsonValueKind.String
                && int.TryParse(countElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStringCount))
            {
                count = Math.Max(1, parsedStringCount);
            }
        }

        var timestamp = GetString(element, "timestamp");
        var attributesJson = element.TryGetProperty("attributes", out var rawAttributes)
            && rawAttributes.ValueKind != JsonValueKind.Undefined
            ? rawAttributes.GetRawText()
            : "{}";

        if (!HasIdentifiableData(caller, callee, route))
        {
            return (null, "trace entry has no caller, callee, or route");
        }

        return (new CbmNormalizedTraceEntry(
            Caller: caller,
            Callee: callee,
            Service: service,
            TargetService: targetService,
            Route: route,
            Method: method,
            StatusCode: statusCode,
            DurationMs: durationMs,
            Count: count,
            Timestamp: timestamp,
            AttributesJson: attributesJson), null);
    }

    public static bool IsErrorStatus(string? statusCode)
    {
        if (string.IsNullOrWhiteSpace(statusCode))
        {
            return false;
        }

        return int.TryParse(statusCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            && code >= 400;
    }

    public static bool HasUnresolvedSymbols(CbmNormalizedTraceEntry entry, long? callerNodeId, long? calleeNodeId)
    {
        var callerUnresolved = !string.IsNullOrEmpty(entry.Caller) && callerNodeId is null;
        var calleeUnresolved = !string.IsNullOrEmpty(entry.Callee) && calleeNodeId is null;
        return callerUnresolved || calleeUnresolved;
    }

    private static bool HasIdentifiableData(string caller, string callee, string route) =>
        !string.IsNullOrWhiteSpace(caller)
        || !string.IsNullOrWhiteSpace(callee)
        || !string.IsNullOrWhiteSpace(route);

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static Dictionary<string, string> CollectAttributes(JsonElement element)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty("attributes", out var attributesElement))
        {
            return attributes;
        }

        if (attributesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in attributesElement.EnumerateObject())
            {
                attributes[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.GetRawText(),
                };
            }

            return attributes;
        }

        if (attributesElement.ValueKind != JsonValueKind.Array)
        {
            return attributes;
        }

        foreach (var item in attributesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = GetString(item, "key");
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var value = GetString(item, "string_value")
                ?? GetString(item, "value")
                ?? (item.TryGetProperty("int_value", out var intValue) ? intValue.GetRawText() : null);
            if (!string.IsNullOrEmpty(value))
            {
                attributes[key] = value;
            }
        }

        return attributes;
    }
}
