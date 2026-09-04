namespace JoinCode.CliCommands;

/// <summary>
/// MCP 工具命令 — bash 直调内部 MCP 工具（call/list/search/schema）
/// <para>ADR: 0065 — 构建完整 DI 容器，从 IMcpToolRegistry 取工具调用，不经过交互模式 REPL</para>
/// </summary>
public sealed class McpCliCommand : Command
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;

    public McpCliCommand() : base("mcp", "调用内部 MCP 工具")
    {
        var jsonOption = new Option<bool>(JccCliArgConstants.Json) { Description = "以 JSON 格式输出结果" };

        var callCommand = new Command("call", "调用指定 MCP 工具");
        var callToolArg = new Argument<string>("tool-name") { Description = "工具名称（如 gh_pr_view）" };
        var argsOption = new Option<string?>("--args") { Description = "工具参数 JSON 内联（如 '{\"repo\":\"x\",\"pr\":1}'）" };
        var argsFileOption = new Option<string?>("--args-file") { Description = "从文件读取参数 JSON" };
        var argsStdinOption = new Option<bool>("--args-stdin") { Description = "从 stdin 读取参数 JSON" };
        callCommand.Add(callToolArg);
        callCommand.Add(argsOption);
        callCommand.Add(argsFileOption);
        callCommand.Add(argsStdinOption);
        callCommand.Add(jsonOption);
        callCommand.SetAction(async (parseResult, ct) =>
        {
            var toolName = parseResult.GetValue(callToolArg);
            var args = parseResult.GetValue(argsOption);
            var argsFile = parseResult.GetValue(argsFileOption);
            var argsStdin = parseResult.GetValue(argsStdinOption);
            var json = parseResult.GetValue(jsonOption);
            return await ExecuteCallAsync(toolName, args, argsFile, argsStdin, json, ct).ConfigureAwait(false);
        });

        var listCommand = new Command("list", "列出所有已注册工具");
        var categoryOption = new Option<string?>("--category") { Description = "按分类过滤（如 github、system、file）" };
        listCommand.Add(categoryOption);
        listCommand.Add(jsonOption);
        listCommand.SetAction(async (parseResult, ct) =>
        {
            var category = parseResult.GetValue(categoryOption);
            var json = parseResult.GetValue(jsonOption);
            return await ExecuteListAsync(category, json, ct).ConfigureAwait(false);
        });

        var searchCommand = new Command("search", "搜索工具");
        var searchQueryArg = new Argument<string>("query") { Description = "搜索查询（关键词或 select:A,B）" };
        searchCommand.Add(searchQueryArg);
        searchCommand.Add(jsonOption);
        searchCommand.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(searchQueryArg);
            var json = parseResult.GetValue(jsonOption);
            return await ExecuteSearchAsync(query, json, ct).ConfigureAwait(false);
        });

        var schemaCommand = new Command("schema", "输出工具参数 Schema");
        var schemaToolArg = new Argument<string>("tool-name") { Description = "工具名称" };
        schemaCommand.Add(schemaToolArg);
        schemaCommand.Add(jsonOption);
        schemaCommand.SetAction(async (parseResult, ct) =>
        {
            var toolName = parseResult.GetValue(schemaToolArg);
            var json = parseResult.GetValue(jsonOption);
            return await ExecuteSchemaAsync(toolName, json, ct).ConfigureAwait(false);
        });

        Add(callCommand);
        Add(listCommand);
        Add(searchCommand);
        Add(schemaCommand);
    }

    private static async Task<int> ExecuteCallAsync(
        string toolName, string? args, string? argsFile, bool argsStdin, bool json, CancellationToken ct)
    {
        var argDict = ParseArgs(args, argsFile, argsStdin);
        if (argDict is null)
            return OutputError("参数 JSON 解析失败", json);

        using var host = await BuildHostAsync(ct).ConfigureAwait(false);
        var registry = host.Services.GetRequiredService<IMcpToolRegistry>();

        if (!await registry.ContainsToolAsync(toolName, ct).ConfigureAwait(false))
            return OutputError($"未找到工具: {toolName}（用 jcc mcp list 查看已注册工具）", json);

        var result = await registry.ExecuteToolAsync(toolName, argDict, ct).ConfigureAwait(false);
        return OutputResult(result, json);
    }

    private static async Task<int> ExecuteListAsync(string? category, bool json, CancellationToken ct)
    {
        using var host = await BuildHostAsync(ct).ConfigureAwait(false);
        var registry = host.Services.GetRequiredService<IMcpToolRegistry>();
        var tools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);

        if (json)
        {
            var items = tools.Values
                .Where(t => string.IsNullOrEmpty(category) || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                .Select(t => new ToolListItem(t.Name, t.Description, t.Category, t.GroupName, t.Kind.ToString()))
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
    }

    private static async Task<int> ExecuteSearchAsync(string query, bool json, CancellationToken ct)
    {
        using var host = await BuildHostAsync(ct).ConfigureAwait(false);
        var registry = host.Services.GetRequiredService<IMcpToolRegistry>();
        var allTools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);

        var deferredTools = allTools.Values
            .Select(t => new DeferredToolInfo(t.Name, t.Description, null, t.Kind == ToolKind.Mcp, t.Category, t.GroupName))
            .ToList();
        var engine = new ToolSearchEngine(deferredTools);
        var result = engine.Search(query, 20);

        if (json)
        {
            var items = result.MatchedToolNames.Select(name => new
            {
                name,
                description = allTools.TryGetValue(name, out var t) ? t.Description : null,
                category = allTools.TryGetValue(name, out var t2) ? t2.Category : null,
            }).ToList();
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
    }

    private static async Task<int> ExecuteSchemaAsync(string toolName, bool json, CancellationToken ct)
    {
        using var host = await BuildHostAsync(ct).ConfigureAwait(false);
        var registry = host.Services.GetRequiredService<IMcpToolRegistry>();
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
    }

    private static async Task<IHost> BuildHostAsync(CancellationToken ct)
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions { NonInteractive = true, TrustWorkspace = true };
        var result = await EngineSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
        return result.Host;
    }

    private static Dictionary<string, JsonElement>? ParseArgs(string? args, string? argsFile, bool argsStdin)
    {
        string? json = null;
        if (argsStdin)
            json = System.Console.In.ReadToEnd();
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

    private sealed record ToolListItem(string Name, string Description, string? Category, string? GroupName, string Kind);
}
