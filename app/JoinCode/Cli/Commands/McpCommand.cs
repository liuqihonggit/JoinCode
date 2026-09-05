namespace JoinCode.CliCommands;

/// <summary>
/// MCP 工具命令执行器 — 扁平元动词 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 的共享逻辑。
/// <para>ADR: 0069 — 从 System.CommandLine.Command 迁移为纯静态类，子命令路由由 FlatSubCommandRouter 处理。</para>
/// </summary>
public sealed class McpCliCommand
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;

    internal static Task<int> ExecuteCallAsync(
        string toolName, string? args, string? argsFile, bool argsStdin, bool json, CancellationToken ct)
    {
        var argDict = ParseArgs(args, argsFile, argsStdin);
        if (argDict is null)
            return Task.FromResult(OutputError("参数 JSON 解析失败", json));

        return WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            if (!await registry.ContainsToolAsync(toolName, ct).ConfigureAwait(false))
                return OutputError($"未找到工具: {toolName}（用 jcc mcp list 查看已注册工具）", json);
            var result = await registry.ExecuteToolAsync(toolName, argDict, ct).ConfigureAwait(false);
            return OutputResult(result, json);
        }, ct);
    }

    internal static Task<int> ExecuteListAsync(string? category, bool json, CancellationToken ct)
        => WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var tools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);

            if (json)
            {
                var items = tools.Values
                    .Where(t => string.IsNullOrEmpty(category) || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new Cli.Output.CliToolListItem(t.Name, t.Description, t.Category, t.GroupName, t.Kind.ToString()))
                    .ToList();
                var envelope = Cli.Output.CliOutputEnvelope.Success(items, new Cli.Output.CliOutputMeta { TotalCount = items.Count });
                System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope));
            }
            else
            {
                var grouped = tools.Values
                    .Where(t => string.IsNullOrEmpty(category) || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(t => t.Category ?? "(无分类)")
                    .OrderBy(g => g.Key);

                foreach (var g in grouped)
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Info}{g.Key}{AnsiStyleConstants.Reset} ({g.Count()} 个):");
                    foreach (var t in g.OrderBy(t => t.Name))
                        TerminalHelper.WriteLine($"  {t.Name,-40} {t.Description}");
                    TerminalHelper.NewLine();
                }
                TerminalHelper.WriteLine($"总计: {tools.Count} 个工具");
            }
            return 0;
        }, ct);

    internal static Task<int> ExecuteSearchAsync(string query, bool json, CancellationToken ct)
        => WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var allTools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);

            var deferredTools = allTools.Values
                .Select(t => new DeferredToolInfo(t.Name, t.Description, null, t.Kind == ToolKind.Mcp, t.Category, t.GroupName))
                .ToList();
            var engine = new ToolSearchEngine(deferredTools);
        var result = engine.Search(query, 20);

        if (json)
        {
            var items = result.MatchedToolNames.Select(name => new Cli.Output.CliToolSearchItem(
                name,
                allTools.TryGetValue(name, out var t) ? t.Description : null,
                allTools.TryGetValue(name, out var t2) ? t2.Category : null)).ToList();
            var envelope = Cli.Output.CliOutputEnvelope.Success(items, new Cli.Output.CliOutputMeta { TotalCount = items.Count });
            System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope));
        }
        else
        {
            TerminalHelper.WriteLine($"搜索 '{query}' 结果 ({result.MatchedToolNames.Count}/{allTools.Count}):");
            foreach (var name in result.MatchedToolNames)
            {
                if (allTools.TryGetValue(name, out var t))
                    TerminalHelper.WriteLine($"  [{t.Category ?? "?"}] {name}: {t.Description}");
                else
                    TerminalHelper.WriteLine($"  {name}");
            }
        }
        return 0;
    }, ct);

    internal static Task<int> ExecuteSchemaAsync(string toolName, bool json, CancellationToken ct)
        => WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var info = await registry.GetToolInfoAsync(toolName, ct).ConfigureAwait(false);

        if (info is null)
            return OutputError($"未找到工具: {toolName}", json);

        if (json)
        {
            System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info.InputSchema, ContractsJsonContext.Default.ToolSchema));
        }
        else
        {
            TerminalHelper.WriteLine($"工具: {info.Name}");
            TerminalHelper.WriteLine($"描述: {info.Description}");
            TerminalHelper.WriteLine($"分类: {info.Category ?? "(无)"}");
            TerminalHelper.WriteLine($"参数 Schema:");
            System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info.InputSchema, ContractsJsonContext.Default.ToolSchema));
        }
        return 0;
    }, ct);

    internal static async Task<IHost> BuildHostAsync(CancellationToken ct)
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions { NonInteractive = true, TrustWorkspace = true };
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        var result = await EngineSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
        return result.Host;
    }

    internal static async Task<int> ExecuteServeAsync(string transport, int port, string hostName, CancellationToken ct)
    {
        if (!string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteError($"不支持的传输方式: {transport}（仅支持 stdio 或 http）");
            return 1;
        }

        var appHost = await BuildHostAsync(ct).ConfigureAwait(false);
        try
        {
            var registry = appHost.Services.GetRequiredService<IMcpToolRegistry>();
            var toolCount = await registry.GetCountAsync(ct).ConfigureAwait(false);
            var server = new JccMcpServer(registry, "jcc-mcp", "1.0.0",
                $"jcc 内部 MCP 服务端 — 暴露 {toolCount} 个工具");

            if (string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Info}jcc mcp serve{AnsiStyleConstants.Reset} stdio 模式启动，暴露 {toolCount} 个工具");
                await server.RunAsync(ct).ConfigureAwait(false);
                return 0;
            }

            var prefix = $"http://{hostName}:{port}/mcp/";
            var httpServer = new McpHttpServer(server, prefix, statelessMode: true);
            TerminalHelper.WriteLine($"{TerminalColors.Info}jcc mcp serve{AnsiStyleConstants.Reset} HTTP 模式启动: {prefix}，暴露 {toolCount} 个工具");
            TerminalHelper.WriteLine("按 Ctrl+C 停止");
            await httpServer.RunAsync(ct).ConfigureAwait(false);
            httpServer.Dispose();
            return 0;
        }
        finally
        {
            try { appHost.Dispose(); } catch (Exception ex) { Diag.WriteLine($"[McpCommand.serve] Host dispose 异常已忽略: {ex.Message}"); }
        }
    }

    internal static async Task<int> WithHostAsync(Func<IServiceProvider, Task<int>> action, CancellationToken ct)
    {
        var host = await BuildHostAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(host.Services).ConfigureAwait(false);
        }
        finally
        {
            try { host.Dispose(); } catch (Exception ex) { Diag.WriteLine($"[McpCommand] Host dispose 异常已忽略: {ex.Message}"); }
        }
    }

    private static Dictionary<string, JsonElement>? ParseArgs(string? args, string? argsFile, bool argsStdin)
    {
        string? json = null;
        if (argsStdin)
        {
            // 读取 stdin 原始字节，循环去除所有前导 UTF-8 BOM（PowerShell 管道可能注入多个 BOM）
            using var stream = System.Console.OpenStandardInput();
            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            var offset = 0;
            while (bytes.Length - offset >= 3 && bytes[offset] == 0xEF && bytes[offset + 1] == 0xBB && bytes[offset + 2] == 0xBF)
                offset += 3;
            json = System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }
        else if (!string.IsNullOrEmpty(argsFile))
            json = System.IO.File.ReadAllText(argsFile);
        else if (!string.IsNullOrEmpty(args))
            json = args;

        if (string.IsNullOrEmpty(json))
            return new();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                TerminalHelper.WriteError("参数 JSON 必须是对象（{}），不能是数组或标量");
                return null;
            }
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();
            return dict;
        }
        catch (System.Text.Json.JsonException ex)
        {
            TerminalHelper.WriteError($"JSON 解析失败: {ex.Message}");
            return null;
        }
    }

    private static int OutputResult(ToolResult result, bool json)
    {
        if (json)
        {
            var text = result.GetFirstText() ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append("{\"isError\":");
            sb.Append(result.IsError ? "true" : "false");
            sb.Append(",\"text\":\"");
            AppendEscapedJson(sb, text);
            sb.Append("\"}");
            System.Console.WriteLine(sb.ToString());
        }
        else
        {
            var text = result.GetFirstText() ?? "(无文本输出)";
            if (result.IsError)
                TerminalHelper.WriteError(text);
            else
                TerminalHelper.WriteLine(text);
        }
        return result.IsError ? 1 : 0;
    }

    private static int OutputError(string message, bool json)
    {
        if (json)
        {
            var sb = new StringBuilder();
            sb.Append("{\"isError\":true,\"text\":\"");
            AppendEscapedJson(sb, message);
            sb.Append("\"}");
            System.Console.WriteLine(sb.ToString());
        }
        else
        {
            TerminalHelper.WriteError(message);
        }
        return 1;
    }

    private static void AppendEscapedJson(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
    }
}

