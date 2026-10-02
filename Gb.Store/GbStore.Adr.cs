using System.Globalization;
using Gb.Graph;
using Microsoft.Data.Sqlite;

namespace Gb.Store;

public sealed partial class GbStore
{
    public GbAdr? AdrGet(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            """
            SELECT project, summary, created_at, updated_at
            FROM project_summaries
            WHERE project = $project;
            """);
        Add(command, "$project", project);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new GbAdr(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    public bool AdrExists(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand(
            "SELECT 1 FROM project_summaries WHERE project = $project LIMIT 1;");
        Add(command, "$project", project);
        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    public void AdrStore(string project, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(content);

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var command = CreateCommand(
            """
            INSERT INTO project_summaries (project, summary, source_hash, created_at, updated_at)
            VALUES ($project, $summary, '', $createdAt, $updatedAt)
            ON CONFLICT(project) DO UPDATE SET
                summary = excluded.summary,
                updated_at = excluded.updated_at;
            """);
        Add(command, "$project", project);
        Add(command, "$summary", content);
        Add(command, "$createdAt", now);
        Add(command, "$updatedAt", now);
        command.ExecuteNonQuery();
    }

    public bool AdrDelete(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var command = CreateCommand("DELETE FROM project_summaries WHERE project = $project;");
        Add(command, "$project", project);
        return command.ExecuteNonQuery() > 0;
    }

    public GbAdr AdrUpdateSections(string project, IReadOnlyDictionary<string, string> updates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(updates);

        var existing = AdrGet(project)
            ?? throw new InvalidOperationException("no existing ADR to update");

        var sections = new Dictionary<string, string>(
            GbAdrSections.ParseSections(existing.Content),
            StringComparer.Ordinal);

        foreach (var (key, value) in updates)
        {
            sections[key] = value;
        }

        var merged = GbAdrSections.RenderSections(sections);
        if (merged.Length > GbAdrSections.MaxLength)
        {
            throw new InvalidOperationException(
                $"merged ADR exceeds {GbAdrSections.MaxLength} chars ({merged.Length} chars)");
        }

        AdrStore(project, merged);
        return AdrGet(project)
            ?? throw new InvalidOperationException("ADR missing after update");
    }
}
