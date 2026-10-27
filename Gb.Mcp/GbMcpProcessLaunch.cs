using System.Diagnostics;
using System.Reflection;

namespace Gb.Mcp;

internal static class GbMcpProcessLaunch
{
    internal static bool IsDotNetHostExecutable(string executablePath)
    {
        var fileName = GetFileNamePortable(executablePath);
        var hostName = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(hostName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// File name from a path that may use Windows or Unix separators (for detection only).
    /// </summary>
    private static string GetFileNamePortable(string path)
    {
        var normalized = path.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    internal static string GetManagedEntryAssemblyPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(location))
        {
            var fullPath = Path.GetFullPath(location);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        var dllPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "gb-mcp.dll"));
        if (File.Exists(dllPath))
        {
            return dllPath;
        }

        throw new InvalidOperationException("Could not resolve gb-mcp entry assembly for child spawn.");
    }

    /// <summary>
    /// Path to the dotnet host binary. Prefer <see cref="Environment.ProcessPath"/> when it is already dotnet
    /// (works for dotnet, dotnet.exe, and absolute paths on any OS). Fall back to PATH lookup name only when needed.
    /// </summary>
    internal static string ResolveDotNetHostExecutable()
    {
        if (Environment.ProcessPath is { } processPath && IsDotNetHostExecutable(processPath))
        {
            return processPath;
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    internal static bool ShouldSpawnViaDotNetExec()
    {
        if (Environment.ProcessPath is { } processPath)
        {
            return IsDotNetHostExecutable(processPath);
        }

        return GetManagedEntryAssemblyPath().EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    internal static (string FileName, IReadOnlyList<string> Arguments) BuildIndexChildCommand(
        string repoPath,
        string resultFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultFilePath);

        var arguments = new List<string>();
        string fileName;

        if (ShouldSpawnViaDotNetExec())
        {
            fileName = ResolveDotNetHostExecutable();
            arguments.Add("exec");
            arguments.Add(GetManagedEntryAssemblyPath());
        }
        else
        {
            fileName = Environment.ProcessPath
                ?? throw new InvalidOperationException("Environment.ProcessPath is not available.");
        }

        arguments.Add("--index");
        arguments.Add(Path.GetFullPath(repoPath));
        arguments.Add("--result-file");
        arguments.Add(Path.GetFullPath(resultFilePath));

        return (fileName, arguments);
    }

    internal static void ConfigureDotNetExec(ProcessStartInfo startInfo, string assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        startInfo.FileName = ResolveDotNetHostExecutable();
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(Path.GetFullPath(assemblyPath));
    }
}
