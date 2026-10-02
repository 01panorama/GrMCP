using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public static class GbProjectCatalog
{
    public static IReadOnlyList<GbCachedProject> ListProjects()
    {
        var cacheDirectory = GbCachePaths.ResolveCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
        {
            return [];
        }

        var projects = new List<GbCachedProject>();
        foreach (var databasePath in Directory.EnumerateFiles(cacheDirectory, "*.db"))
        {
            var fileName = Path.GetFileName(databasePath);
            if (fileName.StartsWith('_'))
            {
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(fileName);
            if (!GbProjectNaming.IsValidProjectName(projectName))
            {
                continue;
            }

            projects.Add(ReadCachedProject(projectName, databasePath));
        }

        return projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static GbIndexStatus GetIndexStatus(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var databasePath = GbCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            return new GbIndexStatus(projectName, string.Empty, 0, 0, "not_found");
        }

        using var store = GbStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        var nodes = store.CountNodes(projectName);
        var edges = store.CountEdges(projectName);
        var status = nodes > 0 ? "ready" : "empty";
        return new GbIndexStatus(
            projectName,
            project?.RootPath ?? string.Empty,
            nodes,
            edges,
            status);
    }

    public static bool DeleteProject(string projectName)
    {
        return GbCachePaths.DeleteProjectDatabase(projectName);
    }

    private static GbCachedProject ReadCachedProject(string projectName, string databasePath)
    {
        var fileInfo = new FileInfo(databasePath);
        using var store = GbStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        return new GbCachedProject(
            projectName,
            project?.IndexedAt ?? string.Empty,
            project?.RootPath ?? string.Empty,
            fileInfo.Exists ? fileInfo.Length : 0,
            store.CountNodes(projectName),
            store.CountEdges(projectName));
    }
}
