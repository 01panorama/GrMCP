using Cbm.Pipeline;

namespace Cbm.Tests;

public sealed class CbmSearchPathValidatorTests
{
    [Fact]
    public void SearchPathValidator_AllowsAmpersandInPath()
    {
        Assert.True(SearchPathValidator.IsValid("*R&D*.cs"));
        Assert.True(SearchPathValidator.IsValid("/tmp/R&D/Worker.cs"));
    }

    [Fact]
    public void SearchPathValidator_RejectsShellMetacharacters()
    {
        var invalidPaths = new[]
        {
            "repo'path",
            "repo\"path",
            "repo;path",
            "repo|path",
            "repo$path",
            "repo`path",
            "repo<path",
            "repo>path",
            "repo\npath",
            "repo\rpath",
        };

        foreach (var path in invalidPaths)
        {
            Assert.False(SearchPathValidator.IsValid(path));
        }
    }

    [Fact]
    public void SearchPathValidator_AllowsBackslashOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(SearchPathValidator.IsValid(@"C:\dev\repo"));
    }

    [Fact]
    public void SearchPathValidator_RejectsBackslashOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(SearchPathValidator.IsValid(@"C:\dev\repo"));
    }
}
