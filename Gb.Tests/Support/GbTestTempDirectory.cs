using Microsoft.Data.Sqlite;

namespace Gb.Tests;

public sealed class GbTestTempDirectory : IDisposable
{
    private GbTestTempDirectory(string path) => Path = path;

    public string Path { get; }

    public static GbTestTempDirectory Create(string prefix)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new GbTestTempDirectory(path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
