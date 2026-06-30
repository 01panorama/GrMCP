namespace Cbm.Cypher;

public static class CypherSqlPlanner
{
    private const int MaxVariablePathDepth = 10;

    private static readonly HashSet<string> ScalarNodeColumns = new(StringComparer.Ordinal)
    {
        "name",
        "qualified_name",
        "label",
        "file_path",
        "start_line",
        "end_line",
    };

    public static CypherSqlPlan Plan(string query, string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var ast = CypherParserFrontEnd.Parse(query);
        return Plan(ast, project);
    }

    public static CypherSqlPlan Plan(CypherQuery query, string project)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var branchPlans = new List<CypherSqlPlan>();
        for (var branch = query; branch is not null; branch = branch.UnionNext)
        {
            branchPlans.Add(PlanSingleQuery(branch, project));
        }

        if (branchPlans.Count == 1)
        {
            return branchPlans[0];
        }

        return CombineUnionPlans(branchPlans, query.UnionAll);
    }

    private static CypherSqlPlan PlanSingleQuery(CypherQuery query, string project)
    {
        if (query.Patterns.Count == 0)
        {
            throw new CypherPlanException("query has no MATCH patterns");
        }

        if (query.PatternOptional.Any(optional => optional))
        {
            throw new CypherPlanException("OPTIONAL MATCH is not supported by the SQL planner");
        }

        var context = new PlannerContext(project);
        var fromClause = context.PlanPatterns(query.Patterns);
        var whereClause = context.PlanWhere(query.Where?.Root);
        var selectClause = context.PlanReturn(query.Return);
        var groupByClause = context.PlanGroupBy(query.Return);

        var sql = new System.Text.StringBuilder();
        if (context.RecursiveCtes.Count > 0)
        {
            sql.Append("WITH RECURSIVE ");
            sql.Append(string.Join(", ", context.RecursiveCtes));
            sql.Append(' ');
        }

        sql.Append(selectClause);
        sql.Append(' ');
        sql.Append(fromClause);

        var predicates = new List<string>(context.FilterConditions);
        if (!string.IsNullOrEmpty(whereClause))
        {
            predicates.Add(whereClause);
        }

        if (predicates.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", predicates));
        }

        if (!string.IsNullOrEmpty(groupByClause))
        {
            sql.Append(' ');
            sql.Append(groupByClause);
        }

        context.AppendReturnSuffix(sql, query.Return);

        return new CypherSqlPlan(sql.ToString(), context.Parameters);
    }

    private static CypherSqlPlan CombineUnionPlans(IReadOnlyList<CypherSqlPlan> branches, bool unionAll)
    {
        var combinedSql = new System.Text.StringBuilder();
        var combinedParameters = new List<CypherSqlParameter>();
        var separator = unionAll ? " UNION ALL " : " UNION ";

        for (var index = 0; index < branches.Count; index++)
        {
            if (index > 0)
            {
                combinedSql.Append(separator);
            }

            var rewritten = RewriteParameters(branches[index], combinedParameters.Count);
            combinedSql.Append(rewritten.Sql);
            combinedParameters.AddRange(rewritten.Parameters);
        }

        return new CypherSqlPlan(combinedSql.ToString(), combinedParameters);
    }

    private static CypherSqlPlan RewriteParameters(CypherSqlPlan plan, int nameOffset)
    {
        if (plan.Parameters.Count == 0)
        {
            return plan;
        }

        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var rewrittenParameters = new List<CypherSqlParameter>(plan.Parameters.Count);
        for (var index = 0; index < plan.Parameters.Count; index++)
        {
            var oldName = plan.Parameters[index].Name;
            var newName = "$p" + (nameOffset + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            mapping[oldName] = newName;
            rewrittenParameters.Add(new CypherSqlParameter(newName, plan.Parameters[index].Value));
        }

        var sql = plan.Sql;
        foreach (var pair in mapping.OrderByDescending(entry => entry.Key.Length))
        {
            sql = sql.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return new CypherSqlPlan(sql, rewrittenParameters);
    }

    private sealed class PlannerContext
    {
        private int parameterIndex;

        public PlannerContext(string project)
        {
            Project = project;
            AddParameter(project);
        }

        public string Project { get; }

        public string ProjectParameter { get; private set; } = "$p0";

        public List<CypherSqlParameter> Parameters { get; } = [];

        public List<string> RecursiveCtes { get; } = [];

        public List<string> FilterConditions { get; } = [];

        private readonly Dictionary<string, string> nodeAliases = new(StringComparer.Ordinal);

        public string PlanPatterns(IReadOnlyList<CypherPattern> patterns)
        {
            if (patterns.Count == 0)
            {
                throw new CypherPlanException("query has no MATCH patterns");
            }

            var firstPattern = patterns[0];
            if (firstPattern.Nodes.Count == 0)
            {
                throw new CypherPlanException("MATCH pattern has no nodes");
            }

            var sql = new System.Text.StringBuilder();
            var firstNode = firstPattern.Nodes[0];
            var firstAlias = RequireNodeAlias(firstNode);
            sql.Append("FROM nodes ");
            sql.Append(firstAlias);
            AddNodeFilterConditions(firstAlias, firstNode);

            PlanPatternTail(sql, firstPattern, startRelationshipIndex: 0);

            for (var patternIndex = 1; patternIndex < patterns.Count; patternIndex++)
            {
                var pattern = patterns[patternIndex];
                if (pattern.Nodes.Count == 0)
                {
                    continue;
                }

                var anchorNode = pattern.Nodes[0];
                var anchorVariable = anchorNode.Variable ?? string.Empty;
                var anchorAlreadyBound = nodeAliases.ContainsKey(anchorVariable);
                var anchorAlias = anchorAlreadyBound
                    ? nodeAliases[anchorVariable]
                    : RequireNodeAlias(anchorNode);
                if (!anchorAlreadyBound)
                {
                    sql.Append(" INNER JOIN nodes ");
                    sql.Append(anchorAlias);
                    sql.Append(" ON ");
                    sql.Append(anchorAlias);
                    sql.Append(".id = ");
                    sql.Append(anchorAlias);
                    sql.Append(".id");
                    AddNodeFilterConditions(anchorAlias, anchorNode);
                }

                PlanPatternTail(sql, pattern, startRelationshipIndex: 0);
            }

            return sql.ToString();
        }

        private void PlanPatternTail(System.Text.StringBuilder sql, CypherPattern pattern, int startRelationshipIndex)
        {
            for (var relationshipIndex = startRelationshipIndex;
                 relationshipIndex < pattern.Relationships.Count;
                 relationshipIndex++)
            {
                var relationship = pattern.Relationships[relationshipIndex];
                var sourceNode = pattern.Nodes[relationshipIndex];
                var targetNode = pattern.Nodes[relationshipIndex + 1];
                var sourceAlias = RequireNodeAlias(sourceNode);
                var targetVariable = targetNode.Variable ?? string.Empty;
                var targetAlreadyBound = nodeAliases.ContainsKey(targetVariable);
                var targetAlias = targetAlreadyBound
                    ? nodeAliases[targetVariable]
                    : RequireNodeAlias(targetNode);

                if (IsVariableLength(relationship))
                {
                    PlanVariableLengthRelationship(
                        sql,
                        relationship,
                        sourceAlias,
                        targetAlias,
                        relationshipIndex,
                        targetNode,
                        targetAlreadyBound);
                }
                else
                {
                    PlanFixedRelationship(
                        sql,
                        relationship,
                        sourceAlias,
                        targetAlias,
                        relationshipIndex,
                        targetNode,
                        targetAlreadyBound);
                }
            }
        }

        private void PlanFixedRelationship(
            System.Text.StringBuilder sql,
            CypherRelPattern relationship,
            string sourceAlias,
            string targetAlias,
            int relationshipIndex,
            CypherNodePattern targetNode,
            bool targetAlreadyBound)
        {
            var edgeAlias = "e" + relationshipIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sql.Append(" INNER JOIN edges ");
            sql.Append(edgeAlias);
            sql.Append(" ON ");
            sql.Append(edgeAlias);
            sql.Append(".project = ");
            sql.Append(ProjectParameter);
            AppendEdgeDirection(sql, edgeAlias, sourceAlias, targetAlias, relationship.Direction);
            AppendEdgeTypeFilter(sql, edgeAlias, relationship.Types);

            if (!targetAlreadyBound)
            {
                sql.Append(" INNER JOIN nodes ");
                sql.Append(targetAlias);
                sql.Append(" ON ");
                sql.Append(targetAlias);
                sql.Append(".id = ");
                sql.Append(ResolveTargetNodeIdColumn(edgeAlias, relationship.Direction));
                AddNodeFilterConditions(targetAlias, targetNode);
            }
        }

        private void PlanVariableLengthRelationship(
            System.Text.StringBuilder sql,
            CypherRelPattern relationship,
            string sourceAlias,
            string targetAlias,
            int relationshipIndex,
            CypherNodePattern targetNode,
            bool targetAlreadyBound)
        {
            var maxDepth = relationship.MaxHops == 0 ? MaxVariablePathDepth : relationship.MaxHops;
            if (maxDepth > MaxVariablePathDepth)
            {
                throw new CypherPlanException(
                    $"variable-length path exceeds maximum depth of {MaxVariablePathDepth}");
            }

            var cteName = "reach_" + relationshipIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cte = new System.Text.StringBuilder();
            cte.Append(cteName);
            cte.Append("(root_id, end_id, depth) AS (");
            cte.Append("SELECT ");
            cte.Append(sourceAlias);
            cte.Append(".id, ");
            cte.Append(sourceAlias);
            cte.Append(".id, 0");

            cte.Append(" UNION ALL SELECT reach_row.root_id, ");
            cte.Append(NextEndpointExpression(relationship.Direction, "e", "reach_row.end_id"));
            cte.Append(", reach_row.depth + 1 FROM ");
            cte.Append(cteName);
            cte.Append(" reach_row INNER JOIN edges e ON e.project = ");
            cte.Append(ProjectParameter);
            AppendRecursiveEdgeJoin(cte, relationship.Direction, "e", "reach_row.end_id");
            AppendEdgeTypeFilter(cte, "e", relationship.Types, leadingAnd: true);
            cte.Append(" AND reach_row.depth < ");
            cte.Append(AddParameter(maxDepth));
            cte.Append(')');

            RecursiveCtes.Add(cte.ToString());

            var reachAlias = cteName + "_r";
            sql.Append(" INNER JOIN ");
            sql.Append(cteName);
            sql.Append(' ');
            sql.Append(reachAlias);
            sql.Append(" ON ");
            sql.Append(reachAlias);
            sql.Append(".root_id = ");
            sql.Append(sourceAlias);
            sql.Append(".id AND ");
            sql.Append(reachAlias);
            sql.Append(".depth >= ");
            sql.Append(AddParameter(relationship.MinHops));
            sql.Append(" AND ");
            sql.Append(reachAlias);
            sql.Append(".depth <= ");
            sql.Append(AddParameter(maxDepth));

            if (!targetAlreadyBound)
            {
                sql.Append(" INNER JOIN nodes ");
                sql.Append(targetAlias);
                sql.Append(" ON ");
                sql.Append(targetAlias);
                sql.Append(".id = ");
                sql.Append(reachAlias);
                sql.Append(".end_id");
                AddNodeFilterConditions(targetAlias, targetNode);
            }
        }

        public string PlanWhere(CypherExpr? expression)
        {
            return expression is null ? string.Empty : PlanExpression(expression);
        }

        public string PlanReturn(CypherReturnClause? ret)
        {
            if (ret is null)
            {
                var defaultAlias = nodeAliases.Values.FirstOrDefault() ?? "n";
                return "SELECT " + defaultAlias + ".*";
            }

            if (ret.Star)
            {
                var projections = nodeAliases.Values
                    .Select(alias => alias + ".*")
                    .ToArray();
                return "SELECT " + string.Join(", ", projections);
            }

            var items = ret.Items.Select(item => PlanReturnItem(item)).ToList();
            return "SELECT " + string.Join(", ", items);
        }

        public string PlanGroupBy(CypherReturnClause? ret)
        {
            if (ret is null || ret.Star)
            {
                return string.Empty;
            }

            var hasAggregate = ret.Items.Any(item => !string.IsNullOrWhiteSpace(item.Function));
            if (!hasAggregate)
            {
                return string.Empty;
            }

            var groupBy = ret.Items
                .Where(item => string.IsNullOrWhiteSpace(item.Function))
                .Select(PlanGroupByExpression)
                .Where(expression => !string.IsNullOrWhiteSpace(expression))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return groupBy.Count == 0 ? string.Empty : "GROUP BY " + string.Join(", ", groupBy);
        }

        public void AppendReturnSuffix(System.Text.StringBuilder sql, CypherReturnClause? ret)
        {
            if (ret is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(ret.OrderBy))
            {
                sql.Append(" ORDER BY ");
                sql.Append(PlanOrderBy(ret.OrderBy));
                if (!string.IsNullOrWhiteSpace(ret.OrderDirection))
                {
                    sql.Append(' ');
                    sql.Append(ret.OrderDirection);
                }
            }

            if (ret.Skip > 0)
            {
                sql.Append(" OFFSET ");
                sql.Append(ret.Skip.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (ret.Limit > 0)
            {
                sql.Append(" LIMIT ");
                sql.Append(ret.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private string PlanReturnItem(CypherReturnItem item)
        {
            var expression = PlanReturnItemExpression(item);
            var alias = ResolveReturnAlias(item);
            return string.IsNullOrWhiteSpace(alias) ? expression : expression + " AS " + QuoteIdentifier(alias);
        }

        private string PlanReturnItemExpression(CypherReturnItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Function))
            {
                var argument = item.Variable == "*"
                    ? "*"
                    : PlanPropertyReference(item.Variable!, item.Property);
                var aggregate = item.Function.ToUpperInvariant() + "(";
                if (item.Distinct)
                {
                    aggregate += "DISTINCT ";
                }

                aggregate += argument;
                aggregate += ')';
                return aggregate;
            }

            if (!string.IsNullOrWhiteSpace(item.Property))
            {
                return PlanPropertyReference(item.Variable!, item.Property);
            }

            if (!string.IsNullOrWhiteSpace(item.Variable) && nodeAliases.ContainsKey(item.Variable))
            {
                return nodeAliases[item.Variable] + ".*";
            }

            throw new CypherPlanException($"unknown RETURN variable '{item.Variable}'");
        }

        private string PlanGroupByExpression(CypherReturnItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Function))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.Property))
            {
                return PlanPropertyReference(item.Variable!, item.Property);
            }

            if (!string.IsNullOrWhiteSpace(item.Variable) && nodeAliases.ContainsKey(item.Variable))
            {
                return nodeAliases[item.Variable] + ".id";
            }

            throw new CypherPlanException($"unknown RETURN variable '{item.Variable}'");
        }

        private string PlanOrderBy(string orderBy)
        {
            if (orderBy.Contains('(', StringComparison.Ordinal))
            {
                return orderBy;
            }

            var dotIndex = orderBy.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex < 0)
            {
                return QuoteIdentifier(orderBy);
            }

            var variable = orderBy[..dotIndex];
            var property = orderBy[(dotIndex + 1)..];
            return PlanPropertyReference(variable, property);
        }

        private string PlanExpression(CypherExpr expression) =>
            expression.Kind switch
            {
                CypherExprKind.Condition => PlanCondition(expression.Condition!),
                CypherExprKind.And => "(" + PlanExpression(expression.Left!) + " AND " + PlanExpression(expression.Right!) + ")",
                CypherExprKind.Or => "(" + PlanExpression(expression.Left!) + " OR " + PlanExpression(expression.Right!) + ")",
                CypherExprKind.Xor => "(" + PlanExpression(expression.Left!) + " <> " + PlanExpression(expression.Right!) + ")",
                CypherExprKind.Not => "(NOT " + PlanExpression(expression.Left!) + ")",
                _ => throw new CypherPlanException($"unsupported WHERE expression kind '{expression.Kind}'"),
            };

        private string PlanCondition(CypherCondition condition)
        {
            var sql = condition.Operator switch
            {
                "EXISTS" => PlanExists(condition),
                "HAS_LABEL" => PlanHasLabel(condition),
                "IS NULL" => PlanNullCheck(condition, negated: false),
                "IS NOT NULL" => PlanNullCheck(condition, negated: true),
                "IN" => PlanInList(condition),
                "=" => PlanComparison(condition, "="),
                "<>" => PlanComparison(condition, "<>"),
                ">" => PlanComparison(condition, ">"),
                "<" => PlanComparison(condition, "<"),
                ">=" => PlanComparison(condition, ">="),
                "<=" => PlanComparison(condition, "<="),
                "=~" => PlanRegex(condition),
                "CONTAINS" => PlanContains(condition),
                "STARTS WITH" => PlanStartsWith(condition),
                "ENDS WITH" => PlanEndsWith(condition),
                _ => throw new CypherPlanException($"unsupported WHERE operator '{condition.Operator}'"),
            };

            if (condition.Negated)
            {
                sql = "(NOT " + sql + ")";
            }

            return sql;
        }

        private string PlanExists(CypherCondition condition)
        {
            var anchorAlias = RequireBoundNodeAlias(condition.Variable);
            var edgeAlias = "ex_" + anchorAlias;
            var sql = new System.Text.StringBuilder();
            sql.Append("EXISTS (SELECT 1 FROM edges ");
            sql.Append(edgeAlias);
            sql.Append(" WHERE ");
            sql.Append(edgeAlias);
            sql.Append(".project = ");
            sql.Append(ProjectParameter);

            var direction = condition.ExistsDirection ?? CypherExistsDirection.Outbound;
            switch (direction)
            {
                case CypherExistsDirection.Outbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(anchorAlias);
                    sql.Append(".id");
                    break;
                case CypherExistsDirection.Inbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(anchorAlias);
                    sql.Append(".id");
                    break;
                case CypherExistsDirection.Any:
                    sql.Append(" AND (");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(anchorAlias);
                    sql.Append(".id OR ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(anchorAlias);
                    sql.Append(".id)");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(condition.Value))
            {
                sql.Append(" AND ");
                sql.Append(edgeAlias);
                sql.Append(".type = ");
                sql.Append(AddParameter(condition.Value));
            }

            sql.Append(')');
            return sql.ToString();
        }

        private string PlanHasLabel(CypherCondition condition)
        {
            var alias = RequireBoundNodeAlias(condition.Variable);
            return alias + ".label = " + AddParameter(condition.Value!);
        }

        private string PlanNullCheck(CypherCondition condition, bool negated)
        {
            var expression = PlanPropertyReference(condition.Variable, condition.Property);
            return negated ? "(" + expression + " IS NOT NULL)" : "(" + expression + " IS NULL)";
        }

        private string PlanInList(CypherCondition condition)
        {
            var expression = PlanPropertyReference(condition.Variable, condition.Property);
            if (condition.InValues.Count == 0)
            {
                return "0";
            }

            var values = condition.InValues.Select(AddParameter);
            return expression + " IN (" + string.Join(", ", values) + ")";
        }

        private string PlanComparison(CypherCondition condition, string op)
        {
            var left = PlanPropertyReference(condition.Variable, condition.Property);
            var right = AddParameter(ParseLiteralValue(condition.Value));
            return left + " " + op + " " + right;
        }

        private string PlanRegex(CypherCondition condition)
        {
            var left = PlanPropertyReference(condition.Variable, condition.Property);
            return "regexp(" + AddParameter(condition.Value!) + ", " + left + ") = 1";
        }

        private string PlanContains(CypherCondition condition)
        {
            var left = PlanPropertyReference(condition.Variable, condition.Property);
            return "INSTR(" + left + ", " + AddParameter(condition.Value!) + ") > 0";
        }

        private string PlanStartsWith(CypherCondition condition)
        {
            var left = PlanPropertyReference(condition.Variable, condition.Property);
            return left + " LIKE (" + AddParameter(condition.Value!) + " || '%')";
        }

        private string PlanEndsWith(CypherCondition condition)
        {
            var left = PlanPropertyReference(condition.Variable, condition.Property);
            return left + " LIKE ('%' || " + AddParameter(condition.Value!) + ")";
        }

        private string PlanPropertyReference(string variable, string? property)
        {
            var alias = RequireBoundNodeAlias(variable);
            if (string.IsNullOrWhiteSpace(property))
            {
                return alias + ".id";
            }

            if (property is "in_degree" or "out_degree")
            {
                var column = property == "in_degree" ? "target_id" : "source_id";
                return "(SELECT COUNT(*) FROM edges deg WHERE deg.project = " + ProjectParameter +
                       " AND deg." + column + " = " + alias + ".id AND deg.type = 'CALLS')";
            }

            if (ScalarNodeColumns.Contains(property))
            {
                return alias + "." + property;
            }

            return "json_extract(" + alias + ".properties, " + AddParameter("$." + property) + ")";
        }

        private void AddNodeFilterConditions(string alias, CypherNodePattern node)
        {
            FilterConditions.Add(alias + ".project = " + ProjectParameter);

            if (!string.IsNullOrWhiteSpace(node.Label))
            {
                FilterConditions.Add(PlanLabelPredicate(alias, node.Label));
            }

            foreach (var property in node.Properties)
            {
                FilterConditions.Add(PlanInlinePropertyPredicate(alias, property.Key, property.Value));
            }
        }

        private string PlanLabelPredicate(string alias, string labelExpression)
        {
            var labels = labelExpression.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (labels.Length == 1)
            {
                return alias + ".label = " + AddParameter(labels[0]);
            }

            return alias + ".label IN (" + string.Join(", ", labels.Select(AddParameter)) + ")";
        }

        private string PlanInlinePropertyPredicate(string alias, string key, string value)
        {
            if (ScalarNodeColumns.Contains(key))
            {
                return alias + "." + key + " = " + AddParameter(ParseLiteralValue(value));
            }

            return "json_extract(" + alias + ".properties, " + AddParameter("$." + key) + ") = " +
                   AddParameter(ParseLiteralValue(value));
        }

        private static void AppendEdgeDirection(
            System.Text.StringBuilder sql,
            string edgeAlias,
            string sourceAlias,
            string targetAlias,
            CypherRelDirection direction)
        {
            switch (direction)
            {
                case CypherRelDirection.Outbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(sourceAlias);
                    sql.Append(".id AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(targetAlias);
                    sql.Append(".id");
                    break;
                case CypherRelDirection.Inbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(sourceAlias);
                    sql.Append(".id AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(targetAlias);
                    sql.Append(".id");
                    break;
                case CypherRelDirection.Any:
                    sql.Append(" AND ((");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(sourceAlias);
                    sql.Append(".id AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(targetAlias);
                    sql.Append(".id) OR (");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(sourceAlias);
                    sql.Append(".id AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(targetAlias);
                    sql.Append(".id))");
                    break;
            }
        }

        private static void AppendRecursiveEdgeJoin(
            System.Text.StringBuilder sql,
            CypherRelDirection direction,
            string edgeAlias,
            string nodeIdExpression)
        {
            switch (direction)
            {
                case CypherRelDirection.Outbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(nodeIdExpression);
                    break;
                case CypherRelDirection.Inbound:
                    sql.Append(" AND ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(nodeIdExpression);
                    break;
                case CypherRelDirection.Any:
                    sql.Append(" AND (");
                    sql.Append(edgeAlias);
                    sql.Append(".source_id = ");
                    sql.Append(nodeIdExpression);
                    sql.Append(" OR ");
                    sql.Append(edgeAlias);
                    sql.Append(".target_id = ");
                    sql.Append(nodeIdExpression);
                    sql.Append(')');
                    break;
            }
        }

        private void AppendEdgeTypeFilter(
            System.Text.StringBuilder sql,
            string edgeAlias,
            IReadOnlyList<string> types,
            bool leadingAnd = false)
        {
            if (types.Count == 0)
            {
                return;
            }

            if (leadingAnd)
            {
                sql.Append(" AND ");
            }
            else
            {
                sql.Append(" AND ");
            }

            if (types.Count == 1)
            {
                sql.Append(edgeAlias);
                sql.Append(".type = ");
                sql.Append(AddParameter(types[0]));
                return;
            }

            sql.Append(edgeAlias);
            sql.Append(".type IN (");
            sql.Append(string.Join(", ", types.Select(AddParameter)));
            sql.Append(')');
        }

        private static string ResolveTargetNodeIdColumn(string edgeAlias, CypherRelDirection direction) =>
            direction switch
            {
                CypherRelDirection.Outbound => edgeAlias + ".target_id",
                CypherRelDirection.Inbound => edgeAlias + ".source_id",
                CypherRelDirection.Any => edgeAlias + ".target_id",
                _ => edgeAlias + ".target_id",
            };

        private static string NextEndpointExpression(CypherRelDirection direction, string edgeAlias, string nodeIdExpression) =>
            direction switch
            {
                CypherRelDirection.Outbound => edgeAlias + ".target_id",
                CypherRelDirection.Inbound => edgeAlias + ".source_id",
                CypherRelDirection.Any => "CASE WHEN " + edgeAlias + ".source_id = " + nodeIdExpression +
                                           " THEN " + edgeAlias + ".target_id ELSE " + edgeAlias + ".source_id END",
                _ => edgeAlias + ".target_id",
            };

        private string RequireNodeAlias(CypherNodePattern node)
        {
            var variable = node.Variable ?? "_n" + nodeAliases.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!nodeAliases.ContainsKey(variable))
            {
                nodeAliases[variable] = QuoteIdentifier(variable);
            }

            return nodeAliases[variable];
        }

        private string RequireBoundNodeAlias(string variable)
        {
            if (!nodeAliases.TryGetValue(variable, out var alias))
            {
                throw new CypherPlanException($"WHERE references unbound variable '{variable}'");
            }

            return alias;
        }

        private string AddParameter(object? value)
        {
            var name = "$p" + parameterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            parameterIndex++;
            Parameters.Add(new CypherSqlParameter(name, value));
            if (Parameters.Count == 1)
            {
                ProjectParameter = name;
            }

            return name;
        }

        private static string ResolveReturnAlias(CypherReturnItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Alias))
            {
                return item.Alias;
            }

            if (!string.IsNullOrWhiteSpace(item.Function))
            {
                return item.Function.ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(item.Property))
            {
                return item.Variable + "_" + item.Property;
            }

            return item.Variable ?? string.Empty;
        }

        private static bool IsVariableLength(CypherRelPattern relationship) =>
            relationship.MinHops != 1 || relationship.MaxHops is not 1;

        private static string QuoteIdentifier(string identifier)
        {
            if (identifier.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return identifier;
            }

            return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private static object? ParseLiteralValue(string? raw)
        {
            if (raw is null)
            {
                return null;
            }

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return raw;
        }
    }
}
