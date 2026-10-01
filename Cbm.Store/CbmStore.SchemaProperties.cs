using System.Globalization;
using Cbm.Graph;
using Microsoft.Data.Sqlite;

namespace Cbm.Store;

public sealed partial class CbmStore
{
    public void RebuildSchemaProperties(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        using var transaction = connection.BeginTransaction();
        using (var delete = CreateCommand("DELETE FROM schema_properties WHERE project = $project;", transaction))
        {
            Add(delete, "$project", project);
            delete.ExecuteNonQuery();
        }

        using (var insert = CreateCommand(
                   """
                   INSERT INTO schema_properties (project, owner, kind, property_key, value_type)
                   SELECT
                     project,
                     owner,
                     kind,
                     property_key,
                     CASE WHEN COUNT(DISTINCT folded_type) > 1 THEN 'mixed' ELSE MAX(folded_type) END
                   FROM (
                     SELECT
                       n.project AS project,
                       'node' AS owner,
                       n.label AS kind,
                       j.key AS property_key,
                       CASE WHEN j.type IN ('true', 'false') THEN 'boolean' ELSE j.type END AS folded_type
                     FROM nodes AS n
                     CROSS JOIN json_each(n.properties) AS j
                     WHERE n.project = $project
                       AND j.type != 'null'
                     UNION ALL
                     SELECT
                       e.project,
                       'edge',
                       e.type,
                       j.key,
                       CASE WHEN j.type IN ('true', 'false') THEN 'boolean' ELSE j.type END
                     FROM edges AS e
                     CROSS JOIN json_each(e.properties) AS j
                     WHERE e.project = $project
                       AND j.type != 'null'
                   )
                   GROUP BY project, owner, kind, property_key;
                   """,
                   transaction))
        {
            Add(insert, "$project", project);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<CbmSchemaProperty> GetSchemaProperties(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var rows = new List<CbmSchemaProperty>();
        using var command = CreateCommand(
            """
            SELECT owner, kind, property_key, value_type
            FROM schema_properties
            WHERE project = $project
            ORDER BY owner, kind, property_key;
            """);
        Add(command, "$project", project);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CbmSchemaProperty(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }
}
