namespace JoinCode.CliCommands;

/// <summary>
/// MCP 工具命令执行器 — 扁平元动词 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 的共享逻辑。
/// <para>ADR: 0069 — 从 System.CommandLine.Command 迁移为纯静态类，子命令路由由 FlatSubCommandRouter 处理。</para>
/// </summary>
public sealed class McpCliCommand
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;

    internal static Task<int> ExecuteCallAsync(
        string toolName, string? args, string[]? kvArgs, string? argsFile, bool argsStdin, bool json,
        string? vendor = null, string? model = null, CancellationToken ct = default)
    {
        var argDict = ParseArgs(args, kvArgs, argsFile, argsStdin);
        if (argDict is null)
            return Task.FromResult(OutputError("参数解析失败", json));

        return WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            if (!await registry.ContainsToolAsync(toolName, ct).ConfigureAwait(false))
                return OutputError($"未找到工具: {toolName}（用 jcc mcp list 查看已注册工具）", json);
            var result = await registry.ExecuteToolAsync(toolName, argDict, ct).ConfigureAwait(false);
            return OutputResult(result, json);
        }, vendor, model, ct);
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
                System.Console.WriteLine(RelaxedJsonSerializer.Serialize(envelope, JsonCtx));
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
        }, ct: ct);

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
            System.Console.WriteLine(RelaxedJsonSerializer.Serialize(envelope, JsonCtx));
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
    }, ct: ct);

    internal static Task<int> ExecuteSchemaAsync(string toolName, bool json, CancellationToken ct)
        => WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var info = await registry.GetToolInfoAsync(toolName, ct).ConfigureAwait(false);

        if (info is null)
            return OutputError($"未找到工具: {toolName}", json);

        if (json)
        {
            System.Console.WriteLine(RelaxedJsonSerializer.Serialize(info.InputSchema, ContractsJsonContext.Default));
        }
        else
        {
            TerminalHelper.WriteLine($"工具: {info.Name}");
            TerminalHelper.WriteLine($"描述: {info.Description}");
            TerminalHelper.WriteLine($"分类: {info.Category ?? "(无)"}");
            TerminalHelper.WriteLine($"参数 Schema:");
            System.Console.WriteLine(RelaxedJsonSerializer.Serialize(info.InputSchema, ContractsJsonContext.Default));
        }
        return 0;
    }, ct: ct);

    internal static async Task<IHost> BuildHostAsync(string? vendor = null, string? model = null, CancellationToken ct = default)
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions { NonInteractive = true, TrustWorkspace = true, SkipModelFetch = true };
        if (!string.IsNullOrEmpty(vendor))
            options.Vendor = vendor;
        if (!string.IsNullOrEmpty(model))
            options.Model = model;
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        // 子命令模式抑制初始化警告（ShellCapabilityInitializer 的 pwsh/python 检测警告）
        var prevLogLevel = Environment.GetEnvironmentVariable("JCC_LOG_LEVEL");
        Environment.SetEnvironmentVariable("JCC_LOG_LEVEL", "Error");
        try
        {
            var result = await EngineSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
            return result.Host;
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_LOG_LEVEL", prevLogLevel);
        }
    }

    internal static async Task<int> ExecuteServeAsync(string transport, int port, string hostName, CancellationToken ct)
    {
        if (!string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteError($"不支持的传输方式: {transport}（仅支持 stdio 或 http）");
            return 1;
        }

        var appHost = await BuildHostAsync(ct: ct).ConfigureAwait(false);
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

    internal static async Task<int> WithHostAsync(Func<IServiceProvider, Task<int>> action, string? vendor = null, string? model = null, CancellationToken ct = default)
    {
        var host = await BuildHostAsync(vendor, model, ct).ConfigureAwait(false);
        try
        {
            return await action(host.Services).ConfigureAwait(false);
        }
        finally
        {
            try { host.Dispose(); } catch (Exception ex) { Diag.WriteLine($"[McpCommand] Host dispose 异常已忽略: {ex.Message}"); }
        }
    }

    private static Dictionary<string, JsonElement>? ParseArgs(string? args, string[]? kvArgs, string? argsFile, bool argsStdin)
    {
        // 优先级: kvArgs (key=value) > argsJson (JSON) > argsFile > argsStdin
        if (kvArgs is { Length: > 0 })
        {
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in kvArgs)
            {
                var eqIdx = kv.IndexOf('=');
                if (eqIdx <= 0 || eqIdx == kv.Length - 1)
                {
                    TerminalHelper.WriteError($"参数格式错误: '{kv}'，应为 key=value");
                    return null;
                }
                var key = kv[..eqIdx];
                var value = kv[(eqIdx + 1)..];
                dict[key] = ParseValueToJsonElement(value);
            }
            return dict;
        }

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

    /// <summary>
    /// 将 key=value 的字符串值转换为 JsonElement（支持 int/double/bool/string）。
    /// </summary>
    private static JsonElement ParseValueToJsonElement(string value)
    {
        if (int.TryParse(value, out var intVal))
            return JsonDocument.Parse(intVal.ToString()).RootElement.Clone();
        if (double.TryParse(value, out var doubleVal))
            return JsonDocument.Parse(doubleVal.ToString()).RootElement.Clone();
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("true").RootElement.Clone();
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("false").RootElement.Clone();
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("null").RootElement.Clone();
        // 字符串值 — 用 Utf8JsonWriter 写入（AOT 兼容）
        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            writer.WriteStringValue(value);
        ms.Position = 0;
        return JsonDocument.Parse(ms).RootElement.Clone();
    }

    private static int OutputResult(ToolResult result, bool json)
    {
        if (json)
        {
            // json 模式: 输出完整 content 数组(包括文本和图片 base64 数据)
            var sb = new StringBuilder();
            sb.Append("{\"isError\":");
            sb.Append(result.IsError ? "true" : "false");
            sb.Append(",\"content\":[");
            for (int i = 0; i < result.Content.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var c = result.Content[i];
                sb.Append("{\"type\":\"");
                sb.Append(c.Type switch
                {
                    ToolContentType.Image => "image",
                    ToolContentType.Resource => "resource",
                    ToolContentType.Error => "error",
                    ToolContentType.Document => "document",
                    _ => "text",
                });
                sb.Append('"');
                if (!string.IsNullOrEmpty(c.Text))
                {
                    sb.Append(",\"text\":\"");
                    AppendEscapedJson(sb, c.Text);
                    sb.Append('"');
                }
                if (!string.IsNullOrEmpty(c.Data))
                {
                    sb.Append(",\"data\":\"");
                    AppendEscapedJson(sb, c.Data);
                    sb.Append("\",\"mimeType\":\"");
                    AppendEscapedJson(sb, c.MimeType ?? "image/png");
                    sb.Append('"');
                }
                sb.Append('}');
            }
            sb.Append("]}");
            System.Console.WriteLine(sb.ToString());
        }
        else
        {
            // 非 json 模式: 遍历所有 Content,输出文本 + 图片摘要
            var hasOutput = false;
            foreach (var c in result.Content)
            {
                if (!string.IsNullOrEmpty(c.Text))
                {
                    if (result.IsError)
                        TerminalHelper.WriteError(c.Text);
                    else
                        TerminalHelper.WriteLine(c.Text);
                    hasOutput = true;
                }
                else if (!string.IsNullOrEmpty(c.Data))
                {
                    // 图片内容: 输出摘要信息(base64 太长不直接输出到控制台)
                    var mimeType = c.MimeType ?? "unknown";
                    var decodedSize = c.Data.Length * 3 / 4;
                    TerminalHelper.WriteLine($"[图片: {mimeType}, {c.Data.Length} 字节 base64 ≈ {decodedSize} 字节]");
                    hasOutput = true;
                }
            }
            if (!hasOutput)
            {
                var fallback = "(无文本输出)";
                if (result.IsError)
                    TerminalHelper.WriteError(fallback);
                else
                    TerminalHelper.WriteLine(fallback);
            }
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

