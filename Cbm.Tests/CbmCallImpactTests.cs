using Cbm.Pipeline;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmCallImpactTests
{
    [Fact]
    public async Task Propagate_FindsInboundCallerForChangedCalleeFile()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var indexResult = await new IndexRepository().IndexAsync(temp.RootPath);

            var impact = new CallImpactService().Propagate(
                indexResult.ProjectName,
                ["Callee.cs"],
                depth: 2);

            Assert.True(impact.ChangedSymbolCount > 0);
            Assert.Contains(
                impact.ChangedSymbols,
                symbol => symbol.Name == "Target" && symbol.File == "Callee.cs");
            Assert.Contains(
                impact.ImpactedSymbols,
                symbol => symbol.Name == "Run" &&
                    symbol.Direction == "inbound" &&
                    symbol.Hop == 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    [Fact]
    public async Task Propagate_FindsOutboundCalleeForChangedCallerFile()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.RootPath);

        try
        {
            WriteCallerCalleeFixture(temp.RootPath);
            var indexResult = await new IndexRepository().IndexAsync(temp.RootPath);

            var impact = new CallImpactService().Propagate(
                indexResult.ProjectName,
                ["Caller.cs"],
                depth: 2);

            Assert.Contains(
                impact.ImpactedSymbols,
                symbol => symbol.Name == "Target" &&
                    symbol.Direction == "outbound" &&
                    symbol.Hop == 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static void WriteCallerCalleeFixture(string root)
    {
        WriteFile(
            root,
            "Sample.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(
            root,
            "Caller.cs",
            """
            namespace Sample;

            public sealed class Caller
            {
                public string Run() => new Callee().Target();
            }
            """);
        WriteFile(
            root,
            "Callee.cs",
            """
            namespace Sample;

            public sealed class Callee
            {
                public string Target() => "ok";
            }
            """);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static TempDirectory Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cbm-impact-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
