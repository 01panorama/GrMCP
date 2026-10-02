namespace Gb.Tests;

public static class GbTestSampleProjects
{
    public static void WriteSampleProject(string root)
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
    }

    public static void WriteCallerCallee(string root)
    {
        WriteSampleProject(root);
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

    public static void WriteCallerCalleeWithWorker(string root)
    {
        WriteCallerCallee(root);
        WriteFile(
            root,
            "Worker.cs",
            """
            namespace Sample;

            public sealed class Worker
            {
                public string Execute()
                {
                    return "ok";
                }
            }
            """);
    }

    public static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }
}
