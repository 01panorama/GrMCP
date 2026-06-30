using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Cbm.Tests;

[Collection("CbmCache")]
public sealed class CbmMcpSmokeTests
{
    [Fact]
    public async Task StdioServerIndexesRepositoryAndReturnsSnippet()
    {
        using var temp = TempDirectory.Create();
        using var cache = TempDirectory.Create();
        Environment.SetEnvironmentVariable("CBM_CACHE_DIR", cache.Path);

        try
        {
            WriteFile(
                temp.Path,
                "Sample.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            WriteFile(
                temp.Path,
                "Worker.cs",
                """
                namespace Sample;

                public sealed class Worker
                {
                    public string Execute()
                    {
                        return "ok";
                    }
                }
                """);
            WriteFile(
                temp.Path,
                "Caller.cs",
                """
                namespace Sample;

                public sealed class Caller
                {
                    public string Run() => new Callee().Target();
                }
                """);
            WriteFile(
                temp.Path,
                "Callee.cs",
                """
                namespace Sample;

                public sealed class Callee
                {
                    public string Target() => "ok";
                }
                """);

            var serverPath = ResolveMcpExecutablePath();
            await using var server = await McpStdioClient.StartAsync(serverPath, cache.Path);

            var initializeResponse = await server.CallAsync(
                id: 1,
                method: "initialize",
                parameters: new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new { name = "cbm-tests", version = "1.0" },
                });
            Assert.False(initializeResponse.TryGetProperty("error", out _));

            await server.NotifyAsync("notifications/initialized");

            var toolsResponse = await server.CallAsync(id: 2, method: "tools/list", parameters: new { });
            var toolNames = toolsResponse
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("index_repository", toolNames);
            Assert.Contains("list_tools", toolNames);
            Assert.Contains("index_status", toolNames);
            Assert.Contains("list_projects", toolNames);
            Assert.Contains("delete_project", toolNames);
            Assert.Contains("search_graph", toolNames);
            Assert.Contains("get_code_snippet", toolNames);
            Assert.Contains("get_graph_schema", toolNames);
            Assert.Contains("query_graph", toolNames);
            Assert.Contains("get_architecture", toolNames);
            Assert.Contains("trace_path", toolNames);
            Assert.Contains("search_code", toolNames);
            Assert.Contains("manage_adr", toolNames);
            Assert.Contains("ingest_traces", toolNames);
            Assert.Contains("detect_changes", toolNames);
            Assert.Equal(15, toolNames.Count);

            var listToolsResponse = await server.CallToolAsync(
                id: 30,
                name: "list_tools",
                arguments: new { });
            var listToolsText = ExtractToolText(listToolsResponse);
            using var listToolsDocument = JsonDocument.Parse(listToolsText);
            Assert.Equal(15, listToolsDocument.RootElement.GetProperty("total").GetInt32());
            var searchGraphTool = listToolsDocument.RootElement
                .GetProperty("tools")
                .EnumerateArray()
                .First(tool => tool.GetProperty("name").GetString() == "search_graph");
            Assert.True(searchGraphTool.GetProperty("parameters").GetArrayLength() > 0);
            Assert.True(searchGraphTool.TryGetProperty("example_input", out _));

            var listToolsMarkdownResponse = await server.CallToolAsync(
                id: 31,
                name: "list_tools",
                arguments: new { format = "markdown" });
            var listToolsMarkdown = ExtractToolText(listToolsMarkdownResponse);
            Assert.Contains("# CBM MCP Tools", listToolsMarkdown, StringComparison.Ordinal);
            Assert.Contains("## search_graph", listToolsMarkdown, StringComparison.Ordinal);

            var scopedListToolsResponse = await server.CallToolAsync(
                id: 32,
                name: "list_tools",
                arguments: new { tools = new[] { "search_graph", "get_code_snippet" } });
            var scopedListToolsText = ExtractToolText(scopedListToolsResponse);
            using var scopedListToolsDocument = JsonDocument.Parse(scopedListToolsText);
            Assert.Equal(2, scopedListToolsDocument.RootElement.GetProperty("total").GetInt32());
            var scopedToolNames = scopedListToolsDocument.RootElement
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("search_graph", scopedToolNames);
            Assert.Contains("get_code_snippet", scopedToolNames);

            var categoryListToolsResponse = await server.CallToolAsync(
                id: 33,
                name: "list_tools",
                arguments: new { category = "meta" });
            var categoryListToolsText = ExtractToolText(categoryListToolsResponse);
            using var categoryListToolsDocument = JsonDocument.Parse(categoryListToolsText);
            Assert.Equal(1, categoryListToolsDocument.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                "list_tools",
                categoryListToolsDocument.RootElement.GetProperty("tools")[0].GetProperty("name").GetString());

            var indexResponse = await server.CallToolAsync(
                id: 3,
                name: "index_repository",
                arguments: new { repo_path = temp.Path });
            var indexText = ExtractToolText(indexResponse);
            using var indexDocument = JsonDocument.Parse(indexText);
            var projectName = indexDocument.RootElement.GetProperty("project").GetString();
            Assert.False(string.IsNullOrWhiteSpace(projectName));
            Assert.Equal("indexed", indexDocument.RootElement.GetProperty("status").GetString());
            Assert.True(indexDocument.RootElement.GetProperty("nodes").GetInt32() > 0);
            Assert.True(indexDocument.RootElement.GetProperty("file_hashes").GetProperty("new").GetInt32() > 0);

            var searchResponse = await server.CallToolAsync(
                id: 4,
                name: "search_graph",
                arguments: new
                {
                    project = projectName,
                    name_pattern = "Execute",
                    limit = 5,
                });
            var searchText = ExtractToolText(searchResponse);
            using var searchDocument = JsonDocument.Parse(searchText);
            Assert.True(searchDocument.RootElement.GetProperty("total").GetInt32() >= 1);
            var qualifiedName = searchDocument.RootElement
                .GetProperty("results")[0]
                .GetProperty("qualified_name")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(qualifiedName));

            var snippetResponse = await server.CallToolAsync(
                id: 5,
                name: "get_code_snippet",
                arguments: new
                {
                    project = projectName,
                    qualified_name = qualifiedName,
                });
            var snippetText = ExtractToolText(snippetResponse);
            using var snippetDocument = JsonDocument.Parse(snippetText);
            Assert.Contains(
                "Execute",
                snippetDocument.RootElement.GetProperty("source").GetString(),
                StringComparison.Ordinal);

            var queryResponse = await server.CallToolAsync(
                id: 6,
                name: "query_graph",
                arguments: new
                {
                    project = projectName,
                    query = "MATCH (n:Method) RETURN n.name",
                });
            var queryText = ExtractToolText(queryResponse);
            using var queryDocument = JsonDocument.Parse(queryText);
            Assert.True(queryDocument.RootElement.GetProperty("total").GetInt32() >= 1);
            Assert.False(queryDocument.RootElement.TryGetProperty("hint", out _));
            var rowNames = queryDocument.RootElement
                .GetProperty("rows")
                .EnumerateArray()
                .Select(row => row[0].GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Execute", rowNames);

            var architectureResponse = await server.CallToolAsync(
                id: 7,
                name: "get_architecture",
                arguments: new { project = projectName });
            var architectureText = ExtractToolText(architectureResponse);
            using var architectureDocument = JsonDocument.Parse(architectureText);
            Assert.True(architectureDocument.RootElement.GetProperty("total_nodes").GetInt32() > 0);
            Assert.Equal(
                "C#",
                architectureDocument.RootElement.GetProperty("languages")[0].GetProperty("language").GetString());
            var nodeLabels = architectureDocument.RootElement
                .GetProperty("node_labels")
                .EnumerateArray()
                .Select(label => label.GetProperty("label").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Method", nodeLabels);

            var traceInboundResponse = await server.CallToolAsync(
                id: 8,
                name: "trace_path",
                arguments: new
                {
                    project = projectName,
                    function_name = "Target",
                    direction = "inbound",
                });
            var traceInboundText = ExtractToolText(traceInboundResponse);
            using var traceInboundDocument = JsonDocument.Parse(traceInboundText);
            var inboundCallerNames = traceInboundDocument.RootElement
                .GetProperty("callers")
                .EnumerateArray()
                .Select(hop => hop.GetProperty("name").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Run", inboundCallerNames);

            var traceOutboundResponse = await server.CallToolAsync(
                id: 9,
                name: "trace_path",
                arguments: new
                {
                    project = projectName,
                    function_name = "Run",
                    direction = "outbound",
                });
            var traceOutboundText = ExtractToolText(traceOutboundResponse);
            using var traceOutboundDocument = JsonDocument.Parse(traceOutboundText);
            var outboundCalleeNames = traceOutboundDocument.RootElement
                .GetProperty("callees")
                .EnumerateArray()
                .Select(hop => hop.GetProperty("name").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Target", outboundCalleeNames);

            var searchCodeResponse = await server.CallToolAsync(
                id: 10,
                name: "search_code",
                arguments: new
                {
                    project = projectName,
                    pattern = "Target",
                });
            var searchCodeText = ExtractToolText(searchCodeResponse);
            using var searchCodeDocument = JsonDocument.Parse(searchCodeText);
            Assert.True(searchCodeDocument.RootElement.GetProperty("total_results").GetInt32() >= 1);
            Assert.Equal(
                "Target",
                searchCodeDocument.RootElement.GetProperty("results")[0].GetProperty("node").GetString());

            var adrUpdateResponse = await server.CallToolAsync(
                id: 11,
                name: "manage_adr",
                arguments: new
                {
                    project = projectName,
                    mode = "update",
                    content = "## PURPOSE\nSmoke test ADR.\n\n## STACK\nC# port.\n",
                });
            var adrUpdateText = ExtractToolText(adrUpdateResponse);
            using var adrUpdateDocument = JsonDocument.Parse(adrUpdateText);
            Assert.Equal("updated", adrUpdateDocument.RootElement.GetProperty("status").GetString());

            var adrGetResponse = await server.CallToolAsync(
                id: 12,
                name: "manage_adr",
                arguments: new
                {
                    project = projectName,
                    mode = "get",
                });
            var adrGetText = ExtractToolText(adrGetResponse);
            using var adrGetDocument = JsonDocument.Parse(adrGetText);
            Assert.Contains(
                "Smoke test ADR.",
                adrGetDocument.RootElement.GetProperty("content").GetString(),
                StringComparison.Ordinal);

            var adrSectionsResponse = await server.CallToolAsync(
                id: 13,
                name: "manage_adr",
                arguments: new
                {
                    project = projectName,
                    mode = "sections",
                });
            var adrSectionsText = ExtractToolText(adrSectionsResponse);
            using var adrSectionsDocument = JsonDocument.Parse(adrSectionsText);
            var sectionHeaders = adrSectionsDocument.RootElement
                .GetProperty("sections")
                .EnumerateArray()
                .Select(section => section.GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("## PURPOSE", sectionHeaders);

            var ingestResponse = await server.CallToolAsync(
                id: 14,
                name: "ingest_traces",
                arguments: new
                {
                    project = projectName,
                    traces = new[]
                    {
                        new { caller = "Run", callee = "Target", duration_ms = 8.0, count = 1 },
                    },
                });
            var ingestText = ExtractToolText(ingestResponse);
            using var ingestDocument = JsonDocument.Parse(ingestText);
            Assert.Equal("accepted", ingestDocument.RootElement.GetProperty("status").GetString());
            Assert.True(ingestDocument.RootElement.GetProperty("traces_ingested").GetInt32() >= 1);
            Assert.True(ingestDocument.RootElement.GetProperty("edges_matched").GetInt32() >= 1);

            var detectResponse = await server.CallToolAsync(
                id: 15,
                name: "detect_changes",
                arguments: new
                {
                    project = projectName,
                    scope = "files",
                });
            var detectText = ExtractToolText(detectResponse);
            Assert.Contains("not_a_git_repo", detectText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CBM_CACHE_DIR", null);
        }
    }

    private static string ResolveMcpExecutablePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var directory = Path.Combine(current.FullName, "Cbm.Mcp", "bin", "Debug", "net10.0");
            var executable = Path.Combine(directory, "Cbm.Mcp");
            var dll = Path.Combine(directory, "Cbm.Mcp.dll");
            if (File.Exists(executable))
            {
                return executable;
            }

            if (File.Exists(dll))
            {
                return dll;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate Cbm.Mcp for stdio smoke test.");
    }

    private static string ExtractToolText(JsonElement response)
    {
        var content = response.GetProperty("result").GetProperty("content");
        return content[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Tool response did not include text content.");
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cbm-mcp-smoke-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class McpStdioClient : IAsyncDisposable
    {
        private readonly Process process;
        private readonly StreamWriter stdin;
        private readonly StreamReader stdout;
        private readonly StringBuilder stderr = new();
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> pending = new();
        private readonly CancellationTokenSource readerCancellation = new();
        private readonly Task readerTask;

        private McpStdioClient(Process process, StreamWriter stdin, StreamReader stdout)
        {
            this.process = process;
            this.stdin = stdin;
            this.stdout = stdout;
            readerTask = Task.Run(ReadLoopAsync);
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    lock (stderr)
                    {
                        stderr.AppendLine(args.Data);
                    }
                }
            };
            process.BeginErrorReadLine();
        }

        public static async Task<McpStdioClient> StartAsync(string serverPath, string cacheDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["CBM_CACHE_DIR"] = cacheDirectory;

            if (serverPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "dotnet";
                startInfo.Arguments = $"exec \"{serverPath}\"";
            }
            else
            {
                startInfo.FileName = serverPath;
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Cbm.Mcp process.");

            var client = new McpStdioClient(
                process,
                process.StandardInput,
                process.StandardOutput);
            await Task.Delay(250);
            return client;
        }

        public Task<JsonElement> CallAsync(int id, string method, object parameters)
        {
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            WriteMessage(new { jsonrpc = "2.0", id, method, @params = parameters });
            return completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }

        public Task<JsonElement> CallToolAsync(int id, string name, object arguments)
        {
            return CallAsync(
                id,
                "tools/call",
                new
                {
                    name,
                    arguments,
                });
        }

        public Task NotifyAsync(string method)
        {
            WriteMessage(new { jsonrpc = "2.0", method });
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            readerCancellation.Cancel();
            try
            {
                await readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
            readerCancellation.Dispose();
        }

        private void WriteMessage(object payload)
        {
            stdin.WriteLine(JsonSerializer.Serialize(payload));
            stdin.Flush();
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!readerCancellation.IsCancellationRequested)
                {
                    var line = await stdout.ReadLineAsync(readerCancellation.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(line);
                    if (!document.RootElement.TryGetProperty("id", out var idElement))
                    {
                        continue;
                    }

                    if (!idElement.TryGetInt32(out var id) || !pending.Remove(id, out var completion))
                    {
                        continue;
                    }

                    completion.SetResult(document.RootElement.Clone());
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                var errorTail = string.Empty;
                lock (stderr)
                {
                    errorTail = stderr.ToString();
                }

                foreach (var completion in pending.Values)
                {
                    completion.TrySetException(new InvalidOperationException(
                        string.IsNullOrWhiteSpace(errorTail)
                            ? "MCP server exited before responding."
                            : $"MCP server exited before responding. stderr:\n{errorTail}"));
                }

                pending.Clear();
            }
        }
    }
}
