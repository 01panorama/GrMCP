using System.Globalization;
using System.Text.Json;

namespace Cbm.Store;

public static class CbmTraceDuration
{
    private const int UrlPathSlashes = 3;

    public static long ParseDurationNs(string? startNano, string? endNano)
    {
        if (string.IsNullOrEmpty(startNano) || string.IsNullOrEmpty(endNano))
        {
            return 0;
        }

        if (!long.TryParse(startNano, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            || !long.TryParse(endNano, NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return 0;
        }

        return end > start ? end - start : 0;
    }

    public static double? ParseDurationMs(JsonElement? durationMs, JsonElement? durationNs)
    {
        if (durationMs is { } msElement)
        {
            if (msElement.ValueKind == JsonValueKind.Number && msElement.TryGetDouble(out var ms))
            {
                return ms;
            }

            if (msElement.ValueKind == JsonValueKind.String
                && double.TryParse(msElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMs))
            {
                return parsedMs;
            }
        }

        if (durationNs is { } nsElement)
        {
            if (nsElement.ValueKind == JsonValueKind.Number && nsElement.TryGetInt64(out var ns))
            {
                return ns / 1_000_000.0;
            }

            if (nsElement.ValueKind == JsonValueKind.String
                && long.TryParse(nsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNs))
            {
                return parsedNs / 1_000_000.0;
            }
        }

        return null;
    }

    public static string ExtractPathFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var slashes = 0;
        var pathStart = -1;
        for (var i = 0; i < url.Length; i++)
        {
            if (url[i] != '/')
            {
                continue;
            }

            slashes++;
            if (slashes == UrlPathSlashes)
            {
                pathStart = i;
                break;
            }
        }

        if (pathStart < 0)
        {
            return string.Empty;
        }

        var queryIndex = url.IndexOf('?', pathStart);
        return queryIndex >= 0 ? url[pathStart..queryIndex] : url[pathStart..];
    }

    public static long CalculateP99(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(static value => value).ToArray();
        var index = (int)(sorted.Length * 0.99);
        if (index >= sorted.Length)
        {
            index = sorted.Length - 1;
        }

        return sorted[index];
    }
}
