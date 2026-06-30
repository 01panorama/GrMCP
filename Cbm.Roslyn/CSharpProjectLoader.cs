using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Cbm.Roslyn;

public sealed class CSharpProjectLoader
{
    private static readonly object RegistrationLock = new();
    private static bool registeredMsBuild;

    public async Task<CSharpWorkspaceLoadResult> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        RegisterMsBuildDefaults();

        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        var diagnostics = new List<string>();

        using var workspace = MSBuildWorkspace.Create();
        using var workspaceFailedRegistration = workspace.RegisterWorkspaceFailedHandler(
            args => diagnostics.Add(args.Diagnostic.Message));

        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
        {
            var solution = await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var projects = new List<CSharpLoadedProject>();
            foreach (var project in solution.Projects.Where(IsCSharpProject))
            {
                projects.Add(await LoadProjectAsync(project, cancellationToken).ConfigureAwait(false));
            }

            return new CSharpWorkspaceLoadResult(fullPath, projects, diagnostics);
        }

        if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new CSharpWorkspaceLoadResult(
                fullPath,
                [await LoadProjectAsync(project, cancellationToken).ConfigureAwait(false)],
                diagnostics);
        }

        throw new NotSupportedException($"Expected a .sln or .csproj path, got '{fullPath}'.");
    }

    public static void RegisterMsBuildDefaults()
    {
        if (registeredMsBuild || MSBuildLocator.IsRegistered)
        {
            registeredMsBuild = true;
            return;
        }

        lock (RegistrationLock)
        {
            if (registeredMsBuild || MSBuildLocator.IsRegistered)
            {
                registeredMsBuild = true;
                return;
            }

            MSBuildLocator.RegisterDefaults();
            registeredMsBuild = true;
        }
    }

    private static bool IsCSharpProject(Project project)
    {
        return string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);
    }

    private static async Task<CSharpLoadedProject> LoadProjectAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return new CSharpLoadedProject(
                project.Name,
                project.FilePath ?? string.Empty,
                []);
        }

        var documents = new List<CSharpLoadedDocument>();
        foreach (var document in project.Documents.Where(document => document.SupportsSyntaxTree))
        {
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxTree is null || !IsCSharpPath(syntaxTree.FilePath))
            {
                continue;
            }

            documents.Add(new CSharpLoadedDocument(
                project.Name,
                document.FilePath ?? syntaxTree.FilePath,
                syntaxTree,
                compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true)));
        }

        return new CSharpLoadedProject(
            project.Name,
            project.FilePath ?? string.Empty,
            documents);
    }

    private static bool IsCSharpPath(string? path)
    {
        return string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CSharpWorkspaceLoadResult(
    string Path,
    IReadOnlyList<CSharpLoadedProject> Projects,
    IReadOnlyList<string> Diagnostics);

public sealed record CSharpLoadedProject(
    string Name,
    string FilePath,
    IReadOnlyList<CSharpLoadedDocument> Documents);

public sealed record CSharpLoadedDocument(
    string ProjectName,
    string FilePath,
    SyntaxTree SyntaxTree,
    SemanticModel SemanticModel);
