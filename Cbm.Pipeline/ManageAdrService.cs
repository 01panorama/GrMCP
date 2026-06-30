using Cbm.Graph;
using Cbm.Store;

namespace Cbm.Pipeline;

public sealed class ManageAdrService
{
    internal const string EmptyHint =
        "No ADR yet. Create one with manage_adr(mode='update', "
        + "content='## PURPOSE\\n...\\n\\n## STACK\\n...\\n\\n## ARCHITECTURE\\n..."
        + "\\n\\n## PATTERNS\\n...\\n\\n## TRADEOFFS\\n...\\n\\n## PHILOSOPHY\\n...'). "
        + "For guided creation: explore the codebase with get_architecture, "
        + "then draft and store. Sections: PURPOSE, STACK, ARCHITECTURE, "
        + "PATTERNS, TRADEOFFS, PHILOSOPHY.";

    public CbmManageAdrResult Manage(
        string projectName,
        string? mode = null,
        string? content = null,
        IReadOnlyList<string>? sections = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        _ = sections;

        var databasePath = CbmCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("project not found");
        }

        using var store = CbmStore.OpenPath(databasePath);
        var project = store.GetProject(projectName);
        if (project is null)
        {
            throw new InvalidOperationException("project not found");
        }

        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "get" : mode.Trim();
        var adr = EnsureAdrLoaded(store, projectName, project.RootPath);

        if (IsWriteMode(normalizedMode) && content is not null)
        {
            try
            {
                store.AdrStore(projectName, content);
                return new CbmManageAdrResult(Status: "updated");
            }
            catch (Exception)
            {
                return new CbmManageAdrResult(Status: "write_error", IsWriteError: true);
            }
        }

        if (string.Equals(normalizedMode, "sections", StringComparison.Ordinal))
        {
            return new CbmManageAdrResult(
                Sections: CbmAdrSections.ListSectionHeaders(adr?.Content));
        }

        if (adr is not null && !string.IsNullOrEmpty(adr.Content))
        {
            return new CbmManageAdrResult(Content: adr.Content);
        }

        return new CbmManageAdrResult(
            Content: string.Empty,
            Status: "no_adr",
            AdrHint: EmptyHint);
    }

    private static bool IsWriteMode(string mode) =>
        string.Equals(mode, "update", StringComparison.Ordinal)
        || string.Equals(mode, "store", StringComparison.Ordinal);

    private static CbmAdr? EnsureAdrLoaded(CbmStore store, string projectName, string rootPath)
    {
        var adr = store.AdrGet(projectName);
        if (adr is not null)
        {
            return adr;
        }

        var legacyPath = Path.Combine(rootPath, ".codebase-memory", "adr.md");
        if (!File.Exists(legacyPath))
        {
            return null;
        }

        var legacyContent = File.ReadAllText(legacyPath);
        if (string.IsNullOrWhiteSpace(legacyContent))
        {
            return null;
        }

        store.AdrStore(projectName, legacyContent);
        return store.AdrGet(projectName);
    }
}
