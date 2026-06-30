using System.ComponentModel;
using Cbm.Graph;

namespace Cbm.Pipeline;

public sealed class GitDiffService
{
    public CbmGitDiffResult GetChangedFiles(
        string rootPath,
        string baseRef,
        bool includeWorkingTree = true)
    {
        return GetChangedFilesInternal(rootPath, baseRef, includeWorkingTree, includeStatus: false);
    }

    public CbmGitDiffResult GetChangedFilesWithStatus(
        string rootPath,
        string baseRef,
        bool includeWorkingTree = true)
    {
        return GetChangedFilesInternal(rootPath, baseRef, includeWorkingTree, includeStatus: true);
    }

    private static CbmGitDiffResult GetChangedFilesInternal(
        string rootPath,
        string baseRef,
        bool includeWorkingTree,
        bool includeStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);

        if (!GitRefValidator.IsValidRef(baseRef))
        {
            return Failure(
                baseRef,
                errorCode: "invalid_ref",
                hint: "base ref contains invalid characters");
        }

        if (!GitRefValidator.IsValidRepoPath(rootPath))
        {
            return Failure(
                baseRef,
                errorCode: "invalid_ref",
                hint: "repository path contains invalid characters");
        }

        if (!GitProcessRunner.IsGitAvailable())
        {
            return Failure(
                baseRef,
                errorCode: "git_not_installed",
                hint: "Check that git is installed and available on PATH.");
        }

        var context = GitContextResolver.Resolve(rootPath);
        if (!context.IsGit)
        {
            return Failure(
                baseRef,
                errorCode: "not_a_git_repo",
                hint: "The repository path is not inside a git worktree.");
        }

        var repositoryRoot = context.WorktreeRoot ?? Path.GetFullPath(rootPath);
        var threeDotRef = $"{baseRef}...HEAD";

        GitProcessRunner.GitRunResult threeDotResult;
        try
        {
            threeDotResult = GitProcessRunner.RunInRepository(
                repositoryRoot,
                ["diff", "--name-only", threeDotRef]);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return Failure(
                baseRef,
                errorCode: "git_not_installed",
                hint: "Check that git is installed and available on PATH.");
        }

        var changedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in GitProcessRunner.ReadOutputLines(threeDotResult.StandardOutput))
        {
            changedPaths.Add(line);
        }

        GitProcessRunner.GitRunResult? workingTreeResult = null;
        if (includeWorkingTree)
        {
            workingTreeResult = GitProcessRunner.RunInRepository(
                repositoryRoot,
                ["diff", "--name-only"]);
            foreach (var line in GitProcessRunner.ReadOutputLines(workingTreeResult.StandardOutput))
            {
                changedPaths.Add(line);
            }
        }

        if (threeDotResult.ExitCode != 0 && changedPaths.Count == 0)
        {
            return Failure(
                baseRef,
                errorCode: "base_ref_not_found",
                hint: $"Check that branch or ref '{baseRef}' exists.",
                headSha: context.HeadSha);
        }

        var changedFiles = changedPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        IReadOnlyList<CbmGitChangedFile>? changedFilesWithStatus = null;

        if (includeStatus)
        {
            var statusByPath = new Dictionary<string, CbmGitChangedFile>(StringComparer.Ordinal);
            var statusResult = GitProcessRunner.RunInRepository(
                repositoryRoot,
                ["diff", "--name-status", threeDotRef]);
            foreach (var file in GitDiffNameStatusParser.Parse(statusResult.StandardOutput))
            {
                statusByPath[file.Path] = file;
            }

            changedFilesWithStatus = changedFiles
                .Select(path => statusByPath.TryGetValue(path, out var file)
                    ? file
                    : new CbmGitChangedFile(path, CbmGitChangeStatus.Modified))
                .ToArray();
        }

        return new CbmGitDiffResult(
            Success: true,
            ErrorCode: null,
            Hint: null,
            BaseRef: baseRef,
            HeadSha: context.HeadSha,
            ChangedFiles: changedFiles,
            ChangedFilesWithStatus: changedFilesWithStatus);
    }

    private static CbmGitDiffResult Failure(
        string baseRef,
        string errorCode,
        string hint,
        string? headSha = null)
    {
        return new CbmGitDiffResult(
            Success: false,
            ErrorCode: errorCode,
            Hint: hint,
            BaseRef: baseRef,
            HeadSha: headSha,
            ChangedFiles: [],
            ChangedFilesWithStatus: null);
    }
}
