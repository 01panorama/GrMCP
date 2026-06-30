using Cbm.Graph;
using Cbm.Pipeline;

namespace Cbm.Tests;

public sealed class CbmGitTests
{
    [Fact]
    public void GitRefValidator_RejectsShellMetacharacters()
    {
        Assert.False(GitRefValidator.IsValidRef("main;rm -rf"));
        Assert.False(GitRefValidator.IsValidRef("$(whoami)"));
        Assert.False(GitRefValidator.IsValidRef(null));
        Assert.False(GitRefValidator.IsValidRef(""));
        Assert.False(GitRefValidator.IsValidRef("   "));
    }

    [Fact]
    public void GitRefValidator_AllowsBranchWithSlash()
    {
        Assert.True(GitRefValidator.IsValidRef("feature/git-context"));
        Assert.True(GitRefValidator.IsValidRef("main"));
    }

    [Fact]
    public void GitDiffNameStatusParser_ParsesModifiedAddedDeletedRenamed()
    {
        const string input =
            """
            M	internal/store/nodes.go
            A	new_file.go
            D	old_file.go
            R100	src/old.go	src/new.go
            """;

        var files = GitDiffNameStatusParser.Parse(input);

        Assert.Equal(4, files.Count);
        Assert.Equal(CbmGitChangeStatus.Modified, files[0].Status);
        Assert.Equal("internal/store/nodes.go", files[0].Path);
        Assert.Equal(CbmGitChangeStatus.Added, files[1].Status);
        Assert.Equal("new_file.go", files[1].Path);
        Assert.Equal(CbmGitChangeStatus.Deleted, files[2].Status);
        Assert.Equal("old_file.go", files[2].Path);
        Assert.Equal(CbmGitChangeStatus.Renamed, files[3].Status);
        Assert.Equal("src/new.go", files[3].Path);
        Assert.Equal("src/old.go", files[3].OldPath);
    }

    [Fact]
    public void CbmGitBranchNaming_SlugsBranchWithSlash()
    {
        Assert.Equal("feature-git-context", CbmGitBranchNaming.SlugFromBranch("feature/git-context", isDetached: false));
        Assert.Equal("detached", CbmGitBranchNaming.SlugFromBranch("DETACHED", isDetached: true));
        Assert.Equal("working-tree", CbmGitBranchNaming.SlugFromBranch(null, isDetached: false));
    }

    [Fact]
    public void CbmGitBranchNaming_DerivesBranchQualifiedName()
    {
        var context = new CbmGitContext(
            IsGit: true,
            IsWorktree: true,
            IsDetached: false,
            RootExists: true,
            InputPath: "/repo",
            WorktreeRoot: "/repo",
            GitDir: "/repo/.git/worktrees/wt",
            GitCommonDir: "/repo/.git",
            CanonicalRoot: "/repo",
            Branch: "feature/git-context",
            BranchSlug: "feature-git-context",
            HeadSha: "abc",
            BaseSha: "");

        Assert.Equal("proj.__branch__.feature-git-context", CbmGitBranchNaming.DeriveBranchQualifiedName("proj", context));
    }

    [Fact]
    public void GitContextResolver_ReturnsNonGitForPlainDirectory()
    {
        using var temp = TempDirectory.Create();

        var context = GitContextResolver.Resolve(temp.RootPath);

        Assert.False(context.IsGit);
        Assert.True(context.RootExists);
        Assert.Equal(temp.RootPath, context.InputPath);
    }

    [Fact]
    public void GitDiffService_ReturnsNotAGitRepoForPlainDirectory()
    {
        using var temp = TempDirectory.Create();
        var service = new GitDiffService();

        var result = service.GetChangedFiles(temp.RootPath, "main");

        Assert.False(result.Success);
        Assert.Equal("not_a_git_repo", result.ErrorCode);
        Assert.Empty(result.ChangedFiles);
    }

    [Fact]
    public void GitDiffService_RejectsInvalidRef()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        var service = new GitDiffService();
        var result = service.GetChangedFiles(fixture.RepositoryPath, "main;evil");

        Assert.False(result.Success);
        Assert.Equal("invalid_ref", result.ErrorCode);
    }

    [Fact]
    public void GitDiffService_ReturnsChangedFileAgainstBaseBranch()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        fixture.RunGit("checkout", "-b", "feature/change");
        File.WriteAllText(Path.Combine(fixture.RepositoryPath, "src", "Edited.cs"), "class Edited { }");
        fixture.CommitAll("edit on feature");

        var service = new GitDiffService();
        var result = service.GetChangedFiles(fixture.RepositoryPath, "main");

        Assert.True(result.Success);
        Assert.Contains("src/Edited.cs", result.ChangedFiles);
    }

    [Fact]
    public void GitDiffService_IncludesAddedAndDeletedFilesWithStatus()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        fixture.RunGit("checkout", "-b", "feature/add-delete");
        File.WriteAllText(Path.Combine(fixture.RepositoryPath, "src", "Added.cs"), "class Added { }");
        File.Delete(Path.Combine(fixture.RepositoryPath, "src", "Helper.cs"));
        fixture.CommitAll("add and delete");

        var service = new GitDiffService();
        var result = service.GetChangedFilesWithStatus(fixture.RepositoryPath, "main");

        Assert.True(result.Success);
        Assert.NotNull(result.ChangedFilesWithStatus);
        Assert.Contains(result.ChangedFilesWithStatus!, file =>
            file.Path == "src/Added.cs" && file.Status == CbmGitChangeStatus.Added);
        Assert.Contains(result.ChangedFilesWithStatus!, file =>
            file.Path == "src/Helper.cs" && file.Status == CbmGitChangeStatus.Deleted);
    }

    [Fact]
    public void GitDiffService_ReportsRenamedFileWithStatus()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        fixture.RunGit("checkout", "-b", "feature/rename");
        fixture.RunGit("mv", "src/Helper.cs", "src/RenamedHelper.cs");
        fixture.CommitAll("rename helper");

        var service = new GitDiffService();
        var result = service.GetChangedFilesWithStatus(fixture.RepositoryPath, "main");

        Assert.True(result.Success);
        Assert.Contains(result.ChangedFilesWithStatus!, file =>
            file.Path == "src/RenamedHelper.cs"
            && file.Status == CbmGitChangeStatus.Renamed
            && file.OldPath == "src/Helper.cs");
    }

    [Fact]
    public void GitDiffService_IncludesUnstagedWorkingTreeChanges()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        File.WriteAllText(Path.Combine(fixture.RepositoryPath, "src", "App.cs"), "class App { int x = 1; }");

        var service = new GitDiffService();
        var result = service.GetChangedFiles(fixture.RepositoryPath, "main");

        Assert.True(result.Success);
        Assert.Contains("src/App.cs", result.ChangedFiles);
    }

    [Fact]
    public void GitDiffService_ReturnsBaseRefNotFoundForMissingRef()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        var service = new GitDiffService();
        var result = service.GetChangedFiles(fixture.RepositoryPath, "nonexistent-ref-xyz");

        Assert.False(result.Success);
        Assert.Equal("base_ref_not_found", result.ErrorCode);
        Assert.Contains("nonexistent-ref-xyz", result.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void GitContextResolver_ReportsDetachedHead()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        fixture.RunGit("checkout", "--detach", "HEAD");

        var context = GitContextResolver.Resolve(fixture.RepositoryPath);

        Assert.True(context.IsGit);
        Assert.True(context.IsDetached);
        Assert.Equal("detached", context.BranchSlug);
    }

    [Fact]
    public void GitContextResolver_ReportsLinkedWorktree()
    {
        using var fixture = GitTestFixture.TryCreate();
        if (fixture is null)
        {
            return;
        }

        var worktreePath = Path.Combine(fixture.TempRoot, "wt with space");
        fixture.RunGit("worktree", "add", "-b", "feature/git-context", worktreePath);

        var mainContext = GitContextResolver.Resolve(fixture.RepositoryPath);
        var worktreeContext = GitContextResolver.Resolve(worktreePath);

        Assert.False(mainContext.IsWorktree);
        Assert.True(worktreeContext.IsWorktree);
        Assert.Equal(mainContext.CanonicalRoot, worktreeContext.CanonicalRoot);
        Assert.Equal("feature/git-context", worktreeContext.Branch);
        Assert.Equal("feature-git-context", worktreeContext.BranchSlug);
        Assert.Equal(
            "proj.__branch__.feature-git-context",
            CbmGitBranchNaming.DeriveBranchQualifiedName("proj", worktreeContext));
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static TempDirectory Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "cbm-git-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class GitTestFixture : IDisposable
    {
        private GitTestFixture(string tempRoot, string repositoryPath)
        {
            TempRoot = tempRoot;
            RepositoryPath = repositoryPath;
        }

        public string TempRoot { get; }

        public string RepositoryPath { get; }

        public static GitTestFixture? TryCreate()
        {
            if (!GitProcessRunner.IsGitAvailable())
            {
                return null;
            }

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "cbm-git-fixture-" + Guid.NewGuid().ToString("N"));
            var repositoryPath = Path.Combine(tempRoot, "repo with space");
            Directory.CreateDirectory(Path.Combine(repositoryPath, "src"));

            var fixture = new GitTestFixture(tempRoot, repositoryPath);
            fixture.RunGit("init");
            fixture.RunGit("checkout", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryPath, "src", "App.cs"), "class App { }");
            File.WriteAllText(Path.Combine(repositoryPath, "src", "Helper.cs"), "class Helper { }");
            fixture.CommitAll("initial");
            return fixture;
        }

        public void RunGit(params string[] arguments)
        {
            var result = GitProcessRunner.RunInRepository(RepositoryPath, arguments);
            Assert.True(
                result.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed: {result.StandardError}");
        }

        public void CommitAll(string message)
        {
            RunGit("add", "-A");
            RunGit("-c", "user.name=CBM Test", "-c", "user.email=cbm@example.invalid", "commit", "-m", message);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
