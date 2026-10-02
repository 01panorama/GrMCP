using Gb.Pipeline;
using Microsoft.Data.Sqlite;

namespace Gb.Tests;

public class IndexedCallerCalleeFixture : IAsyncLifetime, IDisposable
{
    private readonly GbTestCacheScope cacheScope = GbTestCacheScope.Create();
    private GbTestTempDirectory? repositoryDirectory;
    private readonly bool includeWorker;

    public string RepositoryRoot => repositoryDirectory?.Path
        ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public string ProjectName { get; private set; } = string.Empty;

    public string DatabasePath { get; private set; } = string.Empty;

    public IndexedCallerCalleeFixture()
        : this(includeWorker: false)
    {
    }

    protected IndexedCallerCalleeFixture(bool includeWorker)
    {
        this.includeWorker = includeWorker;
    }

    public async Task InitializeAsync()
    {
        repositoryDirectory = GbTestTempDirectory.Create("gb-indexed-caller-callee");
        cacheScope.SetCacheDirectory();
        if (includeWorker)
        {
            GbTestSampleProjects.WriteCallerCalleeWithWorker(RepositoryRoot);
        }
        else
        {
            GbTestSampleProjects.WriteCallerCallee(RepositoryRoot);
        }

        var indexResult = await new IndexRepository().IndexAsync(RepositoryRoot);
        ProjectName = indexResult.ProjectName;
        DatabasePath = indexResult.DatabasePath;
    }

    public async Task ReindexAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
            TryDelete(DatabasePath + "-wal");
            TryDelete(DatabasePath + "-shm");
        }

        var indexResult = await new IndexRepository().IndexAsync(RepositoryRoot);
        ProjectName = indexResult.ProjectName;
        DatabasePath = indexResult.DatabasePath;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        repositoryDirectory?.Dispose();
        cacheScope.Dispose();
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
