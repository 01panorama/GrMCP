namespace Cbm.Pipeline;

public static class CbmCachePaths
{
    public const string DefaultCacheFolderName = "graph-mcp-dotnet";

    public static string ResolveCacheDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CBM_CACHE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", DefaultCacheFolderName);
    }

    public static string EnsureCacheDirectory()
    {
        var cacheDirectory = ResolveCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    public static string GetProjectDatabasePath(string projectName)
    {
        if (!CbmProjectNaming.IsValidProjectName(projectName))
        {
            throw new ArgumentException($"Invalid project name '{projectName}'.", nameof(projectName));
        }

        return Path.Combine(EnsureCacheDirectory(), projectName + ".db");
    }

    public static bool DeleteProjectDatabase(string projectName)
    {
        if (!CbmProjectNaming.IsValidProjectName(projectName))
        {
            return false;
        }

        var databasePath = Path.Combine(ResolveCacheDirectory(), projectName + ".db");
        if (!File.Exists(databasePath))
        {
            return false;
        }

        File.Delete(databasePath);
        TryDeleteIfExists(databasePath + "-wal");
        TryDeleteIfExists(databasePath + "-shm");
        return true;
    }

    private static void TryDeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
