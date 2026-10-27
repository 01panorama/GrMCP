using Gb.Mcp;

namespace Gb.Tests;

public sealed class GbMcpProcessLaunchTests
{
    [Theory]
    [InlineData("/usr/bin/dotnet")]
    [InlineData("/usr/local/share/dotnet/dotnet")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet")]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    public void IsDotNetHostExecutable_recognizes_dotnet_on_any_os(string executablePath)
    {
        Assert.True(GbMcpProcessLaunch.IsDotNetHostExecutable(executablePath));
    }

    [Theory]
    [InlineData("/usr/bin/gb-mcp")]
    [InlineData(@"C:\build\gb-mcp.exe")]
    [InlineData("gb-mcp")]
    public void IsDotNetHostExecutable_rejects_apphost(string executablePath)
    {
        Assert.False(GbMcpProcessLaunch.IsDotNetHostExecutable(executablePath));
    }
}
