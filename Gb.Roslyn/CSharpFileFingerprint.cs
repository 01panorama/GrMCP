using System.Security.Cryptography;

namespace Gb.Roslyn;

public sealed record GbIndexedFileFingerprint(
    string Path,
    string RelativePath,
    long Size,
    long MtimeNs,
    string Sha256);

public static class CSharpFileFingerprint
{
    public static GbIndexedFileFingerprint Collect(string repoRoot, string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var fullPath = Path.GetFullPath(absolutePath);
        var fileInfo = new FileInfo(fullPath);
        var relativePath = CSharpQualifiedName.ToRelativePath(repoRoot, fullPath);
        var mtimeNs = File.GetLastWriteTimeUtc(fullPath).Ticks * 100;
        var sha256 = ComputeSha256(fullPath);

        return new GbIndexedFileFingerprint(
            fullPath,
            relativePath,
            fileInfo.Length,
            mtimeNs,
            sha256);
    }

    public static IReadOnlyList<GbIndexedFileFingerprint> CollectMany(
        string repoRoot,
        IEnumerable<string> absolutePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(absolutePaths);

        return absolutePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Collect(repoRoot, path))
            .OrderBy(fingerprint => fingerprint.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ComputeSha256(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static GbIndexedFileFingerprint FromDiscovered(DiscoveredCSharpFile file)
    {
        return new GbIndexedFileFingerprint(
            file.Path,
            file.RelativePath,
            file.Size,
            file.MtimeNs,
            file.Sha256);
    }
}
