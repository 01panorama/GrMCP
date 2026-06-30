using Cbm.Graph;
using Cbm.Pipeline;
using Cbm.Roslyn;

namespace Cbm.Tests;

public sealed class CbmFileChangeClassifierTests
{
    private const string RepoRoot = "/repo";

    [Fact]
    public void Classify_TreatsMatchingMtimeAndSizeAsUnchanged()
    {
        CbmIndexedFileFingerprint[] discovered =
            [Fingerprint("src/A.cs", mtimeNs: 10, size: 20, sha256: "hash-a")];
        CbmFileHash[] stored =
            [Stored("src/A.cs", mtimeNs: 10, size: 20, sha256: "hash-a")];

        var result = FileChangeClassifier.Classify(RepoRoot, discovered, stored);

        Assert.Single(result.Unchanged);
        Assert.Empty(result.ChangedOrNew);
        Assert.Empty(result.Deleted);
        Assert.Equal(1, result.Counts.Unchanged);
        Assert.Equal(0, result.Counts.New);
        Assert.Equal(0, result.Counts.Changed);
    }

    [Fact]
    public void Classify_TreatsMissingStoredRowAsNew()
    {
        CbmIndexedFileFingerprint[] discovered =
            [Fingerprint("src/New.cs", mtimeNs: 1, size: 2, sha256: "new")];

        var result = FileChangeClassifier.Classify(
            RepoRoot,
            discovered,
            Array.Empty<CbmFileHash>());

        Assert.Empty(result.Unchanged);
        Assert.Single(result.ChangedOrNew);
        Assert.Equal(1, result.Counts.New);
        Assert.Equal(0, result.Counts.Changed);
    }

    [Fact]
    public void Classify_TreatsDifferentSizeAsChanged()
    {
        CbmIndexedFileFingerprint[] discovered =
            [Fingerprint("src/A.cs", mtimeNs: 10, size: 30, sha256: "hash-b")];
        CbmFileHash[] stored =
            [Stored("src/A.cs", mtimeNs: 10, size: 20, sha256: "hash-a")];

        var result = FileChangeClassifier.Classify(RepoRoot, discovered, stored);

        Assert.Empty(result.Unchanged);
        Assert.Single(result.ChangedOrNew);
        Assert.Equal(1, result.Counts.Changed);
        Assert.Equal(0, result.Counts.New);
    }

    [Fact]
    public void Classify_UsesSha256TieBreakWhenMtimeDiffers()
    {
        CbmIndexedFileFingerprint[] discovered =
            [Fingerprint("src/A.cs", mtimeNs: 99, size: 20, sha256: "same-hash")];
        CbmFileHash[] stored =
            [Stored("src/A.cs", mtimeNs: 10, size: 20, sha256: "same-hash")];

        var result = FileChangeClassifier.Classify(RepoRoot, discovered, stored);

        Assert.Single(result.Unchanged);
        Assert.Empty(result.ChangedOrNew);
    }

    [Fact]
    public void Classify_MarksAbsentStoredFileAsDeletedWhenMissingOnDisk()
    {
        using var temp = TempDirectory.Create();
        CbmFileHash[] stored =
            [Stored("gone.cs", mtimeNs: 1, size: 1, sha256: "gone")];

        var result = FileChangeClassifier.Classify(
            temp.RootPath,
            Array.Empty<CbmIndexedFileFingerprint>(),
            stored);

        Assert.Single(result.Deleted);
        Assert.Equal("gone.cs", result.Deleted[0]);
        Assert.Empty(result.ModeSkipped);
    }

    [Fact]
    public void Classify_MarksAbsentStoredFileAsModeSkippedWhenFileExists()
    {
        using var temp = TempDirectory.Create();
        WriteFile(temp.RootPath, "skipped/Helper.cs", "namespace Skipped; public class Helper {}");
        var relativePath = "skipped/Helper.cs";
        var fingerprint = CSharpFileFingerprint.Collect(
            temp.RootPath,
            System.IO.Path.Combine(temp.RootPath, relativePath));
        CbmFileHash[] stored =
        [
            new CbmFileHash
            {
                Project = "p",
                RelativePath = relativePath,
                Sha256 = fingerprint.Sha256,
                MtimeNs = fingerprint.MtimeNs,
                Size = fingerprint.Size,
            },
        ];

        var result = FileChangeClassifier.Classify(
            temp.RootPath,
            Array.Empty<CbmIndexedFileFingerprint>(),
            stored);

        Assert.Empty(result.Deleted);
        Assert.Single(result.ModeSkipped);
        Assert.Equal(relativePath, result.ModeSkipped[0].RelativePath);
    }

    private static CbmIndexedFileFingerprint Fingerprint(
        string relativePath,
        long mtimeNs,
        long size,
        string sha256) =>
        new(
            System.IO.Path.Combine(RepoRoot, relativePath),
            relativePath,
            size,
            mtimeNs,
            sha256);

    private static CbmFileHash Stored(
        string relativePath,
        long mtimeNs,
        long size,
        string sha256) =>
        new()
        {
            Project = "p",
            RelativePath = relativePath,
            MtimeNs = mtimeNs,
            Size = size,
            Sha256 = sha256,
        };

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(root, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
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
            var rootPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cbm-file-change-" + Guid.NewGuid().ToString("N"));
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
