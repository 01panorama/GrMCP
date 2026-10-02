using Microsoft.Data.Sqlite;

namespace Gb.Tests;

public sealed class GbTestCacheScope : IDisposable
{
    private GbTestTempDirectory? cacheDirectory;

    private GbTestCacheScope()
    {
    }

    public string CachePath => cacheDirectory?.Path
        ?? throw new InvalidOperationException("Call SetCacheDirectory before reading CachePath.");

    public static GbTestCacheScope Create() => new();

    public void SetCacheDirectory()
    {
        cacheDirectory ??= GbTestTempDirectory.Create("gb-test-cache");
        Environment.SetEnvironmentVariable("GB_CACHE_DIR", CachePath);
    }

    public void ClearCacheDirectory()
    {
        if (cacheDirectory is null || !Directory.Exists(CachePath))
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.EnumerateFiles(CachePath))
        {
            TryDelete(file);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GB_CACHE_DIR", null);
        cacheDirectory?.Dispose();
        cacheDirectory = null;
        SqliteConnection.ClearAllPools();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
