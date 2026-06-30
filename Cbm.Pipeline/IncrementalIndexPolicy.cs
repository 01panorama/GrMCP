using Cbm.Roslyn;

namespace Cbm.Pipeline;

public static class IncrementalIndexPolicy
{
    public static bool IsNoChange(FileChangeClassification classification)
    {
        return classification.ChangedOrNew.Count == 0 && classification.Deleted.Count == 0;
    }

    public static string? GetFallbackReason(
        bool databaseExists,
        int storedHashCount,
        FileChangeClassification classification,
        int discoveredCount)
    {
        if (!databaseExists || storedHashCount == 0)
        {
            return "initial_index";
        }

        if (discoveredCount > storedHashCount + storedHashCount / 2)
        {
            return "discovery_count_exceeded";
        }

        if (HasProjectGraphChange(classification))
        {
            return "project_graph_changed";
        }

        return null;
    }

    private static bool HasProjectGraphChange(FileChangeClassification classification)
    {
        foreach (var file in classification.ChangedOrNew)
        {
            if (IsProjectGraphFile(file.RelativePath))
            {
                return true;
            }
        }

        foreach (var deleted in classification.Deleted)
        {
            if (IsProjectGraphFile(deleted))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProjectGraphFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
    }
}
