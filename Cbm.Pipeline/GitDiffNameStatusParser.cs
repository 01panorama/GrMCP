using Cbm.Graph;

namespace Cbm.Pipeline;

public static class GitDiffNameStatusParser
{
    public static IReadOnlyList<CbmGitChangedFile> Parse(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var files = new List<CbmGitChangedFile>();
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

    private static bool TryParseLine(string line, out CbmGitChangedFile file)
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
        CbmGitChangeStatus status;

        if (statusToken.Length > 0 && statusToken[0] == 'R')
        {
            var secondTab = remainder.IndexOf('\t', StringComparison.Ordinal);
            if (secondTab < 0)
            {
                return false;
            }

            oldPath = remainder[..secondTab];
            path = remainder[(secondTab + 1)..];
            status = CbmGitChangeStatus.Renamed;
        }
        else
        {
            path = remainder;
            status = statusToken[0] switch
            {
                'A' => CbmGitChangeStatus.Added,
                'D' => CbmGitChangeStatus.Deleted,
                'M' => CbmGitChangeStatus.Modified,
                _ => CbmGitChangeStatus.Modified,
            };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        file = new CbmGitChangedFile(path, status, oldPath);
        return true;
    }
}
