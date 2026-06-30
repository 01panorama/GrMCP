using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore
{
    public void UpsertFileHash(CbmFileHash fileHash)
    {
        UpsertFileHashBatch([fileHash]);
    }

    public IReadOnlyList<CbmFileHash> GetFileHashes(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT project, rel_path, sha256, mtime_ns, size
            FROM file_hashes
            WHERE project = $project
            ORDER BY rel_path;
            """);
        Add(command, "$project", project);

        var results = new List<CbmFileHash>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CbmFileHash
            {
                Project = reader.GetString(0),
                RelativePath = reader.GetString(1),
                Sha256 = reader.GetString(2),
                MtimeNs = reader.GetInt64(3),
                Size = reader.GetInt64(4),
            });
        }

        return results;
    }

    public bool DeleteFileHash(string project, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        using var command = CreateCommand(
            "DELETE FROM file_hashes WHERE project = $project AND rel_path = $relPath;");
        Add(command, "$project", project);
        Add(command, "$relPath", relativePath);
        return command.ExecuteNonQuery() > 0;
    }

    public int DeleteFileHashes(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand("DELETE FROM file_hashes WHERE project = $project;");
        Add(command, "$project", project);
        return command.ExecuteNonQuery();
    }
}
