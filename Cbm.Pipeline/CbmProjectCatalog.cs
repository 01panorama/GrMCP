using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public static class CbmProjectCatalog
{
    public static IReadOnlyList<CbmCachedProject> ListProjects()
    {
        var cacheDirectory = CbmCachePaths.ResolveCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
        {
            return [];
        }

        var projects = new List<CbmCachedProject>();
        foreach (var databasePath in Directory.EnumerateFiles(cacheDirectory, "*.db"))
        {
            var fileName = Path.GetFileName(databasePath);
            if (fileName.StartsWith('_'))
            {
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(fileName);
            if (!CbmProjectNaming.IsValidProjectName(projectName))
            {
                continue;
            }

            projects.Add(ReadCachedProject(projectName, databasePath));
        }

        return projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static CbmIndexStatus GetIndexStatus(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            return new CbmIndexStatus(projectName, string.Empty, 0, 0, "not_found");
        }

        using var store = CbmStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        var nodes = store.CountNodes(projectName);
        var edges = store.CountEdges(projectName);
        var status = nodes > 0 ? "ready" : "empty";
        return new CbmIndexStatus(
            projectName,
            project?.RootPath ?? string.Empty,
            nodes,
            edges,
            status);
    }

    public static bool DeleteProject(string projectName)
    {
        return CbmCachePaths.DeleteProjectDatabase(projectName);
    }

    private static CbmCachedProject ReadCachedProject(string projectName, string databasePath)
    {
        var fileInfo = new FileInfo(databasePath);
        using var store = CbmStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        return new CbmCachedProject(
            projectName,
            project?.IndexedAt ?? string.Empty,
            project?.RootPath ?? string.Empty,
            fileInfo.Exists ? fileInfo.Length : 0,
            store.CountNodes(projectName),
            store.CountEdges(projectName));
    }
}
