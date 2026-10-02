using Gb.Store;

namespace Gb.Pipeline;

public static class GbCachePaths
{
    public const string DefaultCacheFolderName = "graphbase-dotnet";

    public static string ResolveCacheDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("GB_CACHE_DIR");
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
        if (!GbProjectNaming.IsValidProjectName(projectName))
        {
            throw new ArgumentException($"Invalid project name '{projectName}'.", nameof(projectName));
        }

        return Path.Combine(EnsureCacheDirectory(), projectName + ".db");
    }

    public static GbStore OpenProjectStore(string projectName)
    {
        var databasePath = GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Project database not found for '{projectName}'.", databasePath);
        }

        return GbStore.OpenPath(databasePath);
    }

    public static bool DeleteProjectDatabase(string projectName)
    {
        if (!GbProjectNaming.IsValidProjectName(projectName))
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
