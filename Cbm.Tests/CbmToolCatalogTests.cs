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

        Assert.Equal(15, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("list_tools", names);
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
