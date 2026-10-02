namespace Gb.Tests;

public sealed class GbTestCacheClassFixture : IDisposable
{
    private readonly GbTestCacheScope scope = GbTestCacheScope.Create();

    public GbTestCacheClassFixture()
    {
        scope.SetCacheDirectory();
    }

    public string CachePath => scope.CachePath;

    public void ClearCacheDirectory() => scope.ClearCacheDirectory();

    public void Dispose() => scope.Dispose();
}
