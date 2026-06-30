using Cbm.Graph;
using Cbm.Roslyn;

namespace Cbm.Pipeline;

public sealed record FileChangeCounts(
    int Unchanged,
    int Changed,
    int New,
    int Deleted,
    int ModeSkipped,
    int Total)
{
    public static FileChangeCounts FromClassification(FileChangeClassification classification)
    {
        var discovered = classification.Unchanged.Count + classification.ChangedOrNew.Count;
        var newCount = classification.ChangedOrNew.Count - classification.ChangedCount;
        return new FileChangeCounts(
            classification.Unchanged.Count,
            classification.ChangedCount,
            newCount,
            classification.Deleted.Count,
            classification.ModeSkipped.Count,
            discovered);
    }
}

public sealed record FileChangeClassification(
    IReadOnlyList<CbmIndexedFileFingerprint> Unchanged,
    IReadOnlyList<CbmIndexedFileFingerprint> ChangedOrNew,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<CbmFileHash> ModeSkipped,
    int ChangedCount)
{
    public FileChangeCounts Counts => FileChangeCounts.FromClassification(this);
}

public static class FileChangeClassifier
{
    public static FileChangeClassification Classify(
        string repoRoot,
        IReadOnlyList<CbmIndexedFileFingerprint> discovered,
        IReadOnlyList<CbmFileHash> storedHashes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(storedHashes);

        var storedByPath = storedHashes.ToDictionary(
            hash => hash.RelativePath,
            StringComparer.Ordinal);
        var discoveredByPath = discovered.ToDictionary(
            file => file.RelativePath,
            StringComparer.Ordinal);

        var unchanged = new List<CbmIndexedFileFingerprint>();
        var changedOrNew = new List<CbmIndexedFileFingerprint>();
        var changedCount = 0;

        foreach (var file in discovered)
        {
            if (!storedByPath.TryGetValue(file.RelativePath, out var stored))
            {
                changedOrNew.Add(file);
                continue;
            }

            if (IsUnchanged(file, stored))
            {
                unchanged.Add(file);
                continue;
            }

            changedOrNew.Add(file);
            changedCount++;
        }

        var deleted = new List<string>();
        var modeSkipped = new List<CbmFileHash>();

        foreach (var stored in storedHashes)
        {
            if (discoveredByPath.ContainsKey(stored.RelativePath))
            {
                continue;
            }

            var classification = ClassifyAbsentStoredFile(repoRoot, stored);
            if (classification == AbsentStoredClassification.Deleted)
            {
                deleted.Add(stored.RelativePath);
            }
            else
            {
                modeSkipped.Add(stored);
            }
        }

        return new FileChangeClassification(
            unchanged,
            changedOrNew,
            deleted,
            modeSkipped,
            changedCount);
    }

    private static bool IsUnchanged(CbmIndexedFileFingerprint file, CbmFileHash stored)
    {
        if (file.MtimeNs == stored.MtimeNs && file.Size == stored.Size)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(stored.Sha256) &&
            string.Equals(file.Sha256, stored.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static AbsentStoredClassification ClassifyAbsentStoredFile(string repoRoot, CbmFileHash stored)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return AbsentStoredClassification.ModeSkipped;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, stored.RelativePath));
        var expectedRelative = CSharpQualifiedName.ToRelativePath(repoRoot, absolutePath);
        if (!string.Equals(expectedRelative, stored.RelativePath, StringComparison.Ordinal))
        {
            return AbsentStoredClassification.ModeSkipped;
        }

        return File.Exists(absolutePath)
            ? AbsentStoredClassification.ModeSkipped
            : AbsentStoredClassification.Deleted;
    }

    private enum AbsentStoredClassification
    {
        Deleted,
        ModeSkipped,
    }
}
