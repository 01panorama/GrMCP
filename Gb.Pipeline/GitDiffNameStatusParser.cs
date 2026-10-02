using Gb.Graph;

namespace Gb.Pipeline;

public static class GitDiffNameStatusParser
{
    public static IReadOnlyList<GbGitChangedFile> Parse(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var files = new List<GbGitChangedFile>();
        using var reader = new StringReader(output);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            if (TryParseLine(line, out var file))
            {
                files.Add(file);
            }
        }

        return files;
    }

    private static bool TryParseLine(string line, out GbGitChangedFile file)
    {
        file = null!;

        var tabIndex = line.IndexOf('\t', StringComparison.Ordinal);
        if (tabIndex <= 0)
        {
            return false;
        }

        var statusToken = line[..tabIndex];
        var remainder = line[(tabIndex + 1)..];

        string path;
        string? oldPath = null;
        GbGitChangeStatus status;

        if (statusToken.Length > 0 && statusToken[0] == 'R')
        {
            var secondTab = remainder.IndexOf('\t', StringComparison.Ordinal);
            if (secondTab < 0)
            {
                return false;
            }

            oldPath = remainder[..secondTab];
            path = remainder[(secondTab + 1)..];
            status = GbGitChangeStatus.Renamed;
        }
        else
        {
            path = remainder;
            status = statusToken[0] switch
            {
                'A' => GbGitChangeStatus.Added,
                'D' => GbGitChangeStatus.Deleted,
                'M' => GbGitChangeStatus.Modified,
                _ => GbGitChangeStatus.Modified,
            };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        file = new GbGitChangedFile(path, status, oldPath);
        return true;
    }
}
