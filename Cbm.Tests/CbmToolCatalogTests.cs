using System.Reflection;
using Cbm.Mcp;
using Cbm.Mcp.Tools;

namespace Cbm.Tests;

public sealed class CbmToolCatalogTests
{
    [Fact]
    public void CatalogContainsExpectedUniqueTools()
    {
        var names = CbmToolCatalog.Tools.Select(tool => tool.Name).ToArray();

        Assert.Equal(16, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("list_tools", names);
        Assert.Contains("get_skill_reference", names);
        Assert.Contains("search_graph", names);
        Assert.Contains("detect_changes", names);
    }

    [Fact]
    public void CatalogMatchesRegisteredMcpTools()
    {
        var catalogNames = CbmToolCatalog.Tools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var registeredNames = typeof(CbmTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes(inherit: false))
            .Where(attribute => attribute.GetType().Name == "McpServerToolAttribute")
            .Select(attribute => attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            catalogNames.OrderBy(name => name, StringComparer.Ordinal),
            registeredNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void RenderMarkdownDocumentsEveryCatalogTool()
    {
        var markdown = CbmToolCatalog.RenderMarkdown();

        Assert.Contains("# CBM MCP Tools", markdown, StringComparison.Ordinal);
        foreach (var tool in CbmToolCatalog.Tools)
        {
            Assert.Contains($"## {tool.Name}", markdown, StringComparison.Ordinal);
            Assert.Contains(tool.Description, markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RenderMarkdownForSubsetOmitsFullSetup()
    {
        var tools = new[]
        {
            CbmToolCatalog.FindByName("search_graph")!,
            CbmToolCatalog.FindByName("get_code_snippet")!,
        };
        var markdown = CbmToolCatalog.RenderMarkdown(tools);

        Assert.Contains("Filtered tool documentation", markdown, StringComparison.Ordinal);
        Assert.Contains("## search_graph", markdown, StringComparison.Ordinal);
        Assert.Contains("## get_code_snippet", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Setup", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## query_graph", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void HighTokenQueryToolsDocumentVerbosity()
    {
        foreach (var name in new[] { "search_graph", "get_code_snippet", "get_architecture", "search_code" })
        {
            var tool = CbmToolCatalog.FindByName(name);
            Assert.NotNull(tool);
            Assert.Contains(tool.Parameters, parameter => parameter.Name == "verbosity");
        }
    }

    [Fact]
    public void CommittedToolsMarkdownDocumentsEveryCatalogTool()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var markdown = File.ReadAllText(Path.Combine(repositoryRoot, "tools.md"));

        Assert.Contains("# CBM MCP Tools", markdown, StringComparison.Ordinal);
        foreach (var tool in CbmToolCatalog.Tools)
        {
            Assert.Contains($"## {tool.Name}", markdown, StringComparison.Ordinal);
            Assert.Contains(tool.Description, markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RenderReferenceIndexesEveryCatalogTool()
    {
        var reference = CbmSkillReference.RenderMarkdown();

        Assert.Contains("# CBM MCP Reference", reference, StringComparison.Ordinal);
        foreach (var tool in CbmToolCatalog.Tools)
        {
            Assert.Contains($"`{tool.Name}`", reference, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReferenceListsVerbosityForEveryVerbosityTool()
    {
        // Scope to the Common Optional Params section; the per-category tables also
        // contain rows that start with "| `search_graph` |".
        var section = ExtractSection(CbmSkillReference.RenderMarkdown(), "## Common Optional Params");
        var rows = section.Split('\n');
        var verbosityTools = CbmToolCatalog.Tools
            .Where(tool => tool.Parameters.Any(parameter => parameter.Name == "verbosity"))
            .Select(tool => tool.Name);

        foreach (var name in verbosityTools)
        {
            var row = rows.FirstOrDefault(line => line.StartsWith($"| `{name}` |", StringComparison.Ordinal));
            Assert.NotNull(row);
            Assert.Contains("`verbosity`", row, StringComparison.Ordinal);
        }
    }

    private static string ExtractSection(string markdown, string heading)
    {
        var normalized = markdown.Replace("\r\n", "\n");
        var start = normalized.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Heading not found: {heading}");
        var next = normalized.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? normalized[start..] : normalized[start..next];
    }

    [Fact]
    public void CommittedReferenceMatchesRenderer()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var committed = File.ReadAllText(Path.Combine(repositoryRoot, "Skill", "reference.md"));

        Assert.Equal(Normalize(CbmSkillReference.RenderMarkdown()), Normalize(committed));
    }

    [Fact]
    public void CommittedToolsMarkdownMatchesRenderer()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var committed = File.ReadAllText(Path.Combine(repositoryRoot, "tools.md"));

        Assert.Equal(Normalize(CbmToolCatalog.RenderMarkdown()), Normalize(committed));
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n");
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "tools.md"))
                && File.Exists(Path.Combine(current.FullName, "Cbm.NET.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing tools.md.");
    }
}
