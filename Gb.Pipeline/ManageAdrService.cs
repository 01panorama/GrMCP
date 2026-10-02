using Gb.Graph;
using Gb.Store;

namespace Gb.Pipeline;

public sealed class ManageAdrService
{
    internal const string EmptyHint =
        "No ADR yet. Create one with manage_adr(mode='update', "
        + "content='## PURPOSE\\n...\\n\\n## STACK\\n...\\n\\n## ARCHITECTURE\\n..."
        + "\\n\\n## PATTERNS\\n...\\n\\n## TRADEOFFS\\n...\\n\\n## PHILOSOPHY\\n...'). "
        + "For guided creation: explore the codebase with get_architecture, "
        + "then draft and store. Sections: PURPOSE, STACK, ARCHITECTURE, "
        + "PATTERNS, TRADEOFFS, PHILOSOPHY.";

    public GbManageAdrResult Manage(
        string projectName,
        string? mode = null,
        string? content = null,
        IReadOnlyList<string>? sections = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        _ = sections;

        var databasePath = GbCachePaths.GetProjectDatabasePath(projectName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("project not found");
        }

        using var store = GbStore.OpenPath(databasePath);
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
                return new GbManageAdrResult(Status: "updated");
            }
            catch (Exception)
            {
                return new GbManageAdrResult(Status: "write_error", IsWriteError: true);
            }
        }

        if (string.Equals(normalizedMode, "sections", StringComparison.Ordinal))
        {
            return new GbManageAdrResult(
                Sections: GbAdrSections.ListSectionHeaders(adr?.Content));
        }

        if (adr is not null && !string.IsNullOrEmpty(adr.Content))
        {
            return new GbManageAdrResult(Content: adr.Content);
        }

        return new GbManageAdrResult(
            Content: string.Empty,
            Status: "no_adr",
            AdrHint: EmptyHint);
    }

    private static bool IsWriteMode(string mode) =>
        string.Equals(mode, "update", StringComparison.Ordinal)
        || string.Equals(mode, "store", StringComparison.Ordinal);

    private static GbAdr? EnsureAdrLoaded(GbStore store, string projectName, string rootPath)
    {
        var adr = store.AdrGet(projectName);
        if (adr is not null)
        {
            return adr;
        }

        var primaryPath = Path.Combine(rootPath, ".graphbase", "adr.md");
        var legacyPath = Path.Combine(rootPath, ".graph-mcp", "adr.md");
        var adrPath = File.Exists(primaryPath) ? primaryPath : legacyPath;
        if (!File.Exists(adrPath))
        {
            return null;
        }

        var legacyContent = File.ReadAllText(adrPath);
        if (string.IsNullOrWhiteSpace(legacyContent))
        {
            return null;
        }

        store.AdrStore(projectName, legacyContent);
        return store.AdrGet(projectName);
    }
}
