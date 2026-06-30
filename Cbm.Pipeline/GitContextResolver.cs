using Cbm.Graph;

namespace Cbm.Pipeline;

public static class GitContextResolver
{
    public static CbmGitContext Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var inputPath = Path.GetFullPath(path);
        var rootExists = Directory.Exists(inputPath) || File.Exists(inputPath);

        if (!GitRefValidator.IsValidRepoPath(inputPath))
        {
            return new CbmGitContext(
                IsGit: false,
                IsWorktree: false,
                IsDetached: false,
                RootExists: rootExists,
                InputPath: inputPath,
                WorktreeRoot: null,
                GitDir: null,
                GitCommonDir: null,
                CanonicalRoot: null,
                Branch: null,
                BranchSlug: null,
                HeadSha: null,
                BaseSha: null);
        }

        if (!GitProcessRunner.TryCapture(inputPath, ["rev-parse", "--show-toplevel"], out var worktreeRoot))
        {
            return new CbmGitContext(
                IsGit: false,
                IsWorktree: false,
                IsDetached: false,
                RootExists: rootExists,
                InputPath: inputPath,
                WorktreeRoot: null,
                GitDir: null,
                GitCommonDir: null,
                CanonicalRoot: null,
                Branch: null,
                BranchSlug: null,
                HeadSha: null,
                BaseSha: null);
        }

        var gitDir = CaptureOrEmpty(inputPath, ["rev-parse", "--git-dir"]);
        var gitCommonDir = CaptureOrEmpty(inputPath, ["rev-parse", "--git-common-dir"]);
        var headSha = CaptureOrEmpty(inputPath, ["rev-parse", "--verify", "HEAD"]);

        string branch;
        var isDetached = false;
        if (GitProcessRunner.TryCapture(inputPath, ["symbolic-ref", "--quiet", "--short", "HEAD"], out var branchName))
        {
            branch = branchName!;
        }
        else
        {
            branch = "DETACHED";
            isDetached = true;
        }

        var canonicalRoot = DeriveCanonicalRoot(worktreeRoot!, gitCommonDir);
        var branchSlug = CbmGitBranchNaming.SlugFromBranch(branch, isDetached);
        var baseSha = CaptureOrEmpty(inputPath, ["merge-base", "HEAD", "@{upstream}"]);
        var isWorktree = !string.IsNullOrEmpty(gitDir)
            && !string.IsNullOrEmpty(gitCommonDir)
            && !string.Equals(gitDir, gitCommonDir, StringComparison.Ordinal);

        return new CbmGitContext(
            IsGit: true,
            IsWorktree: isWorktree,
            IsDetached: isDetached,
            RootExists: rootExists,
            InputPath: inputPath,
            WorktreeRoot: worktreeRoot,
            GitDir: gitDir,
            GitCommonDir: gitCommonDir,
            CanonicalRoot: canonicalRoot,
            Branch: branch,
            BranchSlug: branchSlug,
            HeadSha: headSha,
            BaseSha: baseSha);
    }

    private static string CaptureOrEmpty(string repositoryRoot, IReadOnlyList<string> arguments)
    {
        return GitProcessRunner.TryCapture(repositoryRoot, arguments, out var value)
            ? value!
            : string.Empty;
    }

    private static string DeriveCanonicalRoot(string worktreeRoot, string gitCommonDir)
    {
        var source = !string.IsNullOrEmpty(gitCommonDir) ? gitCommonDir : worktreeRoot;
        var root = Path.IsPathRooted(source)
            ? Path.GetFullPath(source)
            : Path.GetFullPath(Path.Combine(worktreeRoot, source));

        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.EndsWith("/.git", StringComparison.Ordinal) || root.EndsWith("\\.git", StringComparison.Ordinal))
        {
            root = root[..^5];
        }

        return root;
    }
}
