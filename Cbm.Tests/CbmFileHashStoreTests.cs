using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Tests;

public sealed class CbmFileHashStoreTests
{
    private const string Project = "file-hash-store";

    [Fact]
    public void UpsertFileHash_GetRoundTrip()
    {
        using var store = CreateStore();
        var hash = new CbmFileHash
        {
            Project = Project,
            RelativePath = "src/Worker.cs",
            Sha256 = "abc123",
            MtimeNs = 42,
            Size = 100,
        };

        store.UpsertFileHash(hash);
        var rows = store.GetFileHashes(Project);

        Assert.Single(rows);
        Assert.Equal("src/Worker.cs", rows[0].RelativePath);
        Assert.Equal("abc123", rows[0].Sha256);
        Assert.Equal(42, rows[0].MtimeNs);
        Assert.Equal(100, rows[0].Size);
    }

    [Fact]
    public void UpsertFileHashBatch_UpsertsMultipleRows()
    {
        using var store = CreateStore();
        store.UpsertFileHashBatch(
        [
            new CbmFileHash
            {
                Project = Project,
                RelativePath = "a.cs",
                Sha256 = "1",
                MtimeNs = 1,
                Size = 1,
            },
            new CbmFileHash
            {
                Project = Project,
                RelativePath = "b.cs",
                Sha256 = "2",
                MtimeNs = 2,
                Size = 2,
            },
        ]);

        var rows = store.GetFileHashes(Project);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a.cs", "b.cs"], rows.Select(row => row.RelativePath).ToArray());
    }

    [Fact]
    public void UpsertFileHash_OnConflictUpdatesExistingRow()
    {
        using var store = CreateStore();
        store.UpsertFileHash(new CbmFileHash
        {
            Project = Project,
            RelativePath = "src/a.cs",
            Sha256 = "old",
            MtimeNs = 1,
            Size = 10,
        });
        store.UpsertFileHash(new CbmFileHash
        {
            Project = Project,
            RelativePath = "src/a.cs",
            Sha256 = "new",
            MtimeNs = 2,
            Size = 20,
        });

        var row = Assert.Single(store.GetFileHashes(Project));
        Assert.Equal("new", row.Sha256);
        Assert.Equal(2, row.MtimeNs);
        Assert.Equal(20, row.Size);
    }

    [Fact]
    public void DeleteFileHash_RemovesSingleRow()
    {
        using var store = CreateStore();
        store.UpsertFileHashBatch(
        [
            Hash("a.cs"),
            Hash("b.cs"),
        ]);

        Assert.True(store.DeleteFileHash(Project, "a.cs"));
        var rows = store.GetFileHashes(Project);
        Assert.Single(rows);
        Assert.Equal("b.cs", rows[0].RelativePath);
    }

    [Fact]
    public void DeleteFileHashes_RemovesAllProjectRows()
    {
        using var store = CreateStore();
        store.UpsertFileHashBatch([Hash("a.cs"), Hash("b.cs")]);

        Assert.Equal(2, store.DeleteFileHashes(Project));
        Assert.Empty(store.GetFileHashes(Project));
    }

    [Fact]
    public void DeleteProject_CascadesFileHashes()
    {
        using var store = CreateStore();
        store.UpsertFileHash(Hash("a.cs"));

        store.DeleteProject(Project);

        Assert.Empty(store.GetFileHashes(Project));
    }

    private static CbmStore CreateStore()
    {
        var store = CbmStore.OpenMemory();
        store.UpsertProject(Project, "/repo");
        return store;
    }

    private static CbmFileHash Hash(string relativePath) => new()
    {
        Project = Project,
        RelativePath = relativePath,
        Sha256 = relativePath,
        MtimeNs = 1,
        Size = 1,
    };
}
