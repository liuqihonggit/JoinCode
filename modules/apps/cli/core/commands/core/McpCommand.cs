namespace JoinCode.CliCommands;

/// <summary>
/// MCP 工具命令执行器 — 扁平元动词 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 的共享逻辑。
/// <para>ADR: 0069 — 从 System.CommandLine.Command 迁移为纯静态类，子命令路由由 FlatSubCommandRouter 处理。</para>
/// </summary>
public sealed class McpCliCommand {
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;

    internal static async Task<int> ExecuteCallAsync(
        string toolName, string? args, string[]? kvArgs, Dictionary<string, string>? flagArgs,
        string? argsFile, bool argsStdin, bool json,
        string? vendor = null, string? model = null, CancellationToken ct = default) {
        var argDict = await ParseArgsAsync(args, kvArgs, flagArgs, argsFile, argsStdin).ConfigureAwait(false);
        if (argDict is null)
            return OutputError("参数解析失败", json);

        return await ExecuteAgainstRegistryAsync(toolName, argDict, json, vendor, model, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 以已解析好的参数执行工具 — 供 <c>jcc tool call</c> 的 host 路径直连。
    /// <para>不经过 argv 往返：既不会因 shell 转义丢参数，也不会让工具参数名与
    /// 元命令选项（--json / --model 等）撞名 —— 那条路径的参数本来就归工具所有。</para>
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="arguments">工具参数（原始字符串值，按类型推断）。</param>
    /// <param name="json">是否输出 JSON。</param>
    /// <param name="ct">取消令牌。</param>
    internal static async Task<int> ExecuteCallWithArgumentsAsync(
        string toolName, IReadOnlyDictionary<string, string> arguments, bool json, CancellationToken ct = default) {
        var argDict = await BuildArgumentDictionaryAsync(arguments).ConfigureAwait(false);
        return await ExecuteAgainstRegistryAsync(toolName, argDict, json, null, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行工具的核心路径 — 工具存在性检查 → 参数表校验 → 交给注册表执行。
    /// <para>参数校验放在这里（而不是 CLI 层）是因为参数表只有宿主起来之后才拿得到，
    /// 而参数表的真相在注册表里，是唯一数据源。</para>
    /// </summary>
    private static async Task<int> ExecuteAgainstRegistryAsync(
        string toolName, Dictionary<string, JsonElement> argDict, bool json,
        string? vendor, string? model, CancellationToken ct) {
        return await WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            if (!await registry.ContainsToolAsync(toolName, ct).ConfigureAwait(false)) {
                var allTools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);
                var suggestions = LlmJsonHelper.SuggestToolNames(toolName, allTools.Keys);
                var msg = suggestions.Count > 0
                    ? $"未找到工具: {toolName}。是否想用: {string.Join(", ", suggestions)}?（用 jcc mcp_schema <工具名> 查看参数）"
                    : $"未找到工具: {toolName}（用 jcc mcp_list 查看已注册工具）";
                return OutputError(msg, json);
            }
            var schemaError = await ValidateArgumentsAsync(registry, toolName, argDict, ct).ConfigureAwait(false);
            if (schemaError is not null) return OutputError(schemaError, json);
            EnsureShellCapabilityInitialized(services, toolName);
            var result = await registry.ExecuteToolAsync(toolName, argDict, ct).ConfigureAwait(false);
            return OutputResult(result, json);
        }, vendor, model, ct,
            skipToolFilter: RequiresWholeRegistry(toolName),
            toolFilter: name => string.Equals(name, toolName, StringComparison.Ordinal),
            requiredTool: toolName).ConfigureAwait(false);
    }

    /// <summary>
    /// 对照工具参数表校验参数名与必填项 — 未知参数给出真实参数名与相近建议。
    /// <para>取不到参数表时放行（不因 schema 缺失而拦住调用，工具自己会报缺参）。</para>
    /// </summary>
    private static async Task<string?> ValidateArgumentsAsync(
        IMcpToolRegistry registry, string toolName, Dictionary<string, JsonElement> argDict, CancellationToken ct) {
        var info = await registry.GetToolInfoAsync(toolName, ct).ConfigureAwait(false);
        if (info?.InputSchema is null) return null;
        return McpToolArgumentBinder.ValidateAgainstSchema(argDict, info.InputSchema, toolName);
    }

    /// <summary>把原始字符串参数转成 JsonElement 字典（按值形态推断类型）。</summary>
    private static async Task<Dictionary<string, JsonElement>> BuildArgumentDictionaryAsync(
        IReadOnlyDictionary<string, string> arguments) {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments) {
            dict[key] = await ParseValueToJsonElementAsync(value, key).ConfigureAwait(false);
        }
        return dict;
    }

    /// <summary>
    /// shell 执行族工具名 — 与工具注册表一致（bash / powershell / powershell_script 需要系统执行器工厂）
    /// </summary>
    private static readonly FrozenSet<string> ShellExecToolNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "bash", "powershell", "powershell_script");

    /// <summary>
    /// 执行 shell 类工具前确保系统执行器工厂已注册。
    /// <para>
    /// mcp_call 走 <c>SkipChatServices=true</c> 的宿主，不经过会话启动路径，因此
    /// <c>SystemActuatorRegistry</c> 一直是未初始化状态 —— 直接调 <c>jcc mcp_call bash ...</c>
    /// 会报 “SystemActuatorRegistry not initialized”。mcp_serve 在 BuildHostAsync 后补了这一步，
    /// 这里补上同样的初始化，使工具元命令对 shell 工具可用（Initialize 内部有一次性闸门，重复调用无成本）。
    /// </para>
    /// </summary>
    private static void EnsureShellCapabilityInitialized(IServiceProvider services, string toolName) {
        if (!ShellExecToolNames.Contains(toolName))
            return;

        Core.DependencyInjection.ShellCapabilityInitializer.Initialize(
            services.GetRequiredService<IFileSystem>(),
            services.GetService<ILogger<McpCliCommand>>());
    }

    /// <summary>
    /// 需要「全量工具表」才有意义的工具 —— 它们本身就是在查目录，只注册自己会让结果退化成 1 条。
    /// <para>这类调用接受多花约 1 秒全量注册（实测 368 个工具 3.1s vs 单个 2.2s）。</para>
    /// </summary>
    private static bool RequiresWholeRegistry(string toolName)
        => toolName is "tool_search" or "tool_score" or "tool_hypergraph" or "tool_list_templates";

    /// <summary>
    /// mcp_list 的注册表边界说明 — gh_* 工具由独立 jcc-gh.exe 提供, 不在 MCP 注册表。
    /// </summary>
    private static string? BuildRegistryBoundaryNote(string? category)
        => string.IsNullOrEmpty(category) || string.Equals(category, "github", StringComparison.OrdinalIgnoreCase)
            ? "github 组另有 gh_* 工具(172 个, 由独立 jcc-gh.exe 提供, 如 gh_pr_create)不在 MCP 注册表: 用 jcc gh --help 或 jcc gh <分组> --help 查看"
            : null;

    internal static Task<int> ExecuteListAsync(string? category, bool json, CancellationToken ct = default)
        => WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var tools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);
            var boundaryNote = BuildRegistryBoundaryNote(category);

            if (json) {
                var filtered = tools.Values
                    .Where(t => string.IsNullOrEmpty(category) || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var count = filtered.Count;
                var items = filtered
                    .Select(t => new Cli.Output.CliToolListItem(
                        string.IsNullOrEmpty(t.Name) ? CliGuardConfig.DefaultToolName : t.Name,
                        t.Description ?? CliGuardConfig.DefaultToolDescription,
                        t.Category,
                        t.GroupName,
                        t.Kind.ToString()))
                    .ToList();
                var envelope = Cli.Output.CliOutputEnvelope<System.Collections.Generic.List<Cli.Output.CliToolListItem>>.Success(
                    items, new Cli.Output.CliOutputMeta { TotalCount = count, Note = boundaryNote });
                System.Console.WriteLine(envelope.ToJsonString());
            } else {
                var grouped = tools.Values
                    .Where(t => string.IsNullOrEmpty(category) || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(t => t.Category ?? "(无分类)")
                    .OrderBy(g => g.Key);

                foreach (var g in grouped) {
                    TerminalHelper.WriteLine($"{TerminalColors.Info}{g.Key}{AnsiStyleEnumConstants.Reset} ({g.Count()}): {string.Join(", ", g.OrderBy(t => t.Name).Select(t => t.Name))}");
                }
                TerminalHelper.NewLine();
                if (boundaryNote is not null) TerminalHelper.WriteLine($"提示: {boundaryNote}");
                TerminalHelper.WriteLine($"总计: {tools.Count} 个工具（用 jcc mcp_schema <工具名> 查看参数详情）");
            }
            return 0;
        }, ct: ct, skipToolFilter: true);

    internal static Task<int> ExecuteSearchAsync(string query, bool json, CancellationToken ct = default)
        => WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var allTools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);

            var deferredTools = allTools.Values
                .Select(t => new DeferredToolInfo(t.Name, t.Description, null, t.Kind == ToolKind.Mcp, t.Category, t.GroupName))
                .ToList();
            var engine = new ToolSearchEngine(deferredTools);
            var result = engine.Search(query, 20);

            if (json) {
                var count = result.MatchedToolNames.Count;
                var items = result.MatchedToolNames.Select(name => new Cli.Output.CliToolSearchItem(
                    name,
                    allTools.TryGetValue(name, out var t) ? t.Description : null,
                    allTools.TryGetValue(name, out var t2) ? t2.Category : null)).ToList();
                var envelope = Cli.Output.CliOutputEnvelope<System.Collections.Generic.List<Cli.Output.CliToolSearchItem>>.Success(items, new Cli.Output.CliOutputMeta { TotalCount = count });
                System.Console.WriteLine(envelope.ToJsonString());
            } else {
                TerminalHelper.WriteLine($"搜索 '{query}' 结果 ({result.MatchedToolNames.Count}/{allTools.Count}):");
                foreach (var name in result.MatchedToolNames) {
                    if (allTools.TryGetValue(name, out var t))
                        TerminalHelper.WriteLine($"  [{t.Category ?? "?"}] {name}: {t.Description}");
                    else
                        TerminalHelper.WriteLine($"  {name}");
                }
            }
            return 0;
        }, ct: ct, skipToolFilter: true);

    /// <summary>
    /// gh_* 工具的 schema 查询引导 — 这类工具由独立 jcc-gh.exe 提供, MCP 注册表里查不到, 直接报"未找到"会让调用方换命令瞎试。
    /// </summary>
    private static string BuildGhSchemaRedirect(string toolName)
        => $"工具 {toolName} 由独立 jcc-gh.exe（gh CLI 包装器）提供，不在 MCP 注册表。\n"
         + $"查参数: jcc gh <分组> <action> --help（如 jcc gh pr create --help）\n"
         + $"列工具: jcc gh --help 看分组；jcc gh <分组> --help 看该组全部 action";

    internal static Task<int> ExecuteSchemaAsync(string toolName, bool json, CancellationToken ct = default)
        => WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var info = await registry.GetToolInfoAsync(toolName, ct).ConfigureAwait(false);

            if (info is null) {
                if (toolName.StartsWith("gh_", StringComparison.OrdinalIgnoreCase))
                    return OutputError(BuildGhSchemaRedirect(toolName), json);
                var allTools = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);
                var suggestions = LlmJsonHelper.SuggestToolNames(toolName, allTools.Keys);
                var msg = suggestions.Count > 0
                    ? $"未找到工具: {toolName}。是否想用: {string.Join(", ", suggestions)}?（用 jcc mcp_schema <工具名> 查看参数）"
                    : $"未找到工具: {toolName}（用 jcc mcp_list 查看已注册工具）";
                return OutputError(msg, json);
            }

            if (json) {
                System.Console.WriteLine(Cli.Output.CliOutputEnvelope<Abstractions.Tools.ToolSchema>.Success(info.InputSchema, new Cli.Output.CliOutputMeta { TotalCount = 1 }).ToJsonString());
            } else {
                TerminalHelper.WriteLine($"工具: {info.Name}");
                TerminalHelper.WriteLine($"描述: {info.Description}");
                TerminalHelper.WriteLine($"分类: {info.Category ?? "(无)"}");
                TerminalHelper.WriteLine($"参数 Schema:");
                System.Console.WriteLine(RelaxedJsonSerializer.Serialize(info.InputSchema, ContractsJsonContext.Default));
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine(LlmJsonHelper.BuildShellCallExamples(info.Name, info.InputSchema));
            }
            return 0;
        }, ct: ct,
            skipToolFilter: RequiresWholeRegistry(toolName),
            toolFilter: name => string.Equals(name, toolName, StringComparison.Ordinal),
            requiredTool: toolName);

    internal static async Task<IAsyncHost> BuildHostAsync(
        string? vendor = null, string? model = null, CancellationToken ct = default,
        bool skipToolFilter = false, Func<string, bool>? toolFilter = null) {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions {
            NonInteractive = true,
            TrustWorkspace = true,
            SkipModelFetch = true,
            SkipProviderValidation = true,
            SkipChatServices = true,
            SkipProviderConfig = true,
            SkipToolFilter = skipToolFilter,
            ToolNameFilter = toolFilter
        };
        if (!string.IsNullOrEmpty(vendor))
            options = options with { Vendor = vendor };
        if (!string.IsNullOrEmpty(model))
            options = options with { Model = model };
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        // 子命令模式抑制初始化警告（ShellCapabilityInitializer 的 pwsh/python 检测警告）
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        // 工具元命令只需要 IMcpToolRegistry，不读模型供应商配置 —— Hub 不可达是 jcc -p 的门槛，不是执行单个工具的门槛。
        // 默认阈值（JCC_ALLOW_DIRECT_FALLBACK 未设时硬失败）会让 411 个 host 工具在无 Hub 的机器上全部不可用，
        // 这里只在本方法建 Host 期间放行直连配置，退出即恢复，不影响交互会话的默认保护。
        using var fallbackScope = EnvVarScope.Set("JCC_ALLOW_DIRECT_FALLBACK", "1");
        var result = await CliSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
        return result.Host;
    }

    internal static async Task<int> ExecuteServeAsync(string transport, int port, string hostName, CancellationToken ct, int? awaitSeconds = null, string? explicitToken = null) {
        if (!string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase)) {
            TerminalHelper.WriteError($"不支持的传输方式: {transport}（仅支持 stdio 或 http）");
            return 1;
        }

        await using var appHost = await BuildHostAsync(ct: ct, skipToolFilter: true).ConfigureAwait(false);

        // mcp serve 需要 shell 执行器 — BuildHostAsync 跳过了 ShellCapabilityInit（SkipChatServices=true），此处补调
        Core.DependencyInjection.ShellCapabilityInitializer.Initialize(
            appHost.Services.GetRequiredService<IFileSystem>(),
            appHost.Services.GetService<ILogger<JccMcpServer>>());

        var registry = appHost.Services.GetRequiredService<IMcpToolRegistry>();
        var toolCount = await registry.GetCountAsync(ct).ConfigureAwait(false);
        // tools/call 统一走权限管道（13 层中间件），禁止直连注册表绕过权限层 — ADR 0138
        var gateway = appHost.Services.GetRequiredService<IToolExecutionGateway>();
        var server = new JccMcpServer(registry, gateway, "jcc-mcp", "1.0.0",
            $"jcc 内部 MCP 服务端 — 暴露 {toolCount} 个工具");

        // --await N: 子命令级超时,优雅退出并输出结构化信息
        using var serveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (awaitSeconds is { } secs && secs > 0) {
            serveCts.CancelAfter(TimeSpan.FromSeconds(secs));
            TerminalHelper.WriteLine($"{TerminalColors.Info}--await {secs}s{AnsiStyleEnumConstants.Reset} 超时计时器已启动");
        }

        if (string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)) {
            TerminalHelper.WriteLine($"{TerminalColors.Info}jcc mcp serve{AnsiStyleEnumConstants.Reset} stdio 模式启动，暴露 {toolCount} 个工具");
            try {
                await server.RunAsync(serveCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (serveCts.Token.IsCancellationRequested) {
                WriteServeExitReport("stdio", toolCount, 0, 0, TimeSpan.Zero);
            }
            return 0;
        }

        // HTTP 模式: 先尝试 HttpListener,失败降级 TcpListener(纵深防御)
        // token 鉴权: 显式指定用于脚本集成,否则自动生成 — token 是唯一凭证,启动时打印 (ADR 0138)
        var authToken = explicitToken ?? McpAuthToken.Generate();
        var prefix = $"http://{hostName}:{port}/mcp/";
        var startTime = DateTime.UtcNow;
        McpTcpServer? tcpServer = null;
        McpHttpServer? httpServer = null;
        var usedTcp = false;

        try {
            httpServer = new McpHttpServer(server, prefix, statelessMode: true, authToken: authToken);
            httpServer.Start();
            TerminalHelper.WriteLine($"{TerminalColors.Info}jcc mcp serve{AnsiStyleEnumConstants.Reset} HTTP 模式启动(HttpListener): {prefix}，暴露 {toolCount} 个工具");
        } catch (HttpListenerException) {
            // HttpListener 不可用(沙箱/无 HTTP.sys) → 降级 TcpListener
            httpServer?.Dispose();
            httpServer = null;
            tcpServer = new McpTcpServer(server, hostName, port, statelessMode: true, authToken: authToken);
            usedTcp = true;
            TerminalHelper.WriteLine($"{TerminalColors.Info}jcc mcp serve{AnsiStyleEnumConstants.Reset} HTTP 模式启动(TcpListener 降级): http://{hostName}:{port}/mcp/，暴露 {toolCount} 个工具");
        }

        PrintServeTokenUsage(authToken, hostName, prefix);

        TerminalHelper.WriteLine("按 Ctrl+C 停止");

        try {
            if (usedTcp) {
                var tcp = tcpServer ?? throw new InvalidOperationException("TCP 服务器未初始化");
                await tcp.RunAsync(serveCts.Token).ConfigureAwait(false);
            } else {
                var http = httpServer ?? throw new InvalidOperationException("HTTP 服务器未初始化");
                await http.RunAsync(serveCts.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (serveCts.Token.IsCancellationRequested) {
            // 优雅退出
        }

        var uptime = DateTime.UtcNow - startTime;
        var totalReqs = tcpServer?.TotalRequests ?? 0;
        var totalErrs = tcpServer?.TotalErrors ?? 0;
        WriteServeExitReport(usedTcp ? "tcp" : "http", toolCount, totalReqs, totalErrs, uptime);

        tcpServer?.Dispose();
        httpServer?.Dispose();
        return 0;
    }

    /// <summary>
    /// 打印 token 与客户端连接用法 — 无 token 的请求一律 401（ADR 0138: 默认 token 鉴权，无豁免开关）
    /// </summary>
    private static void PrintServeTokenUsage(string authToken, string hostName, string prefix) {
        TerminalHelper.WriteLine($"{TerminalColors.Info}Mcp-Token{AnsiStyleEnumConstants.Reset}: {authToken}");
        if (!hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !hostName.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            TerminalHelper.WriteLine($"{TerminalColors.Warning}警告: 绑定 {hostName} (非 localhost) 会将 MCP 服务端暴露到网络，请确保 token 保密{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLine("客户端连接示例——请求头加 Authorization: Bearer <token>");
    }

    /// <summary>mcp_serve 退出时输出结构化 JSON 报告</summary>
    private static void WriteServeExitReport(string transport, int toolCount, int totalRequests, int totalErrors, TimeSpan uptime) {
        var data = new McpServeExitReport(transport, toolCount, totalRequests, totalErrors, Math.Round(uptime.TotalSeconds, 2));
        var report = Cli.Output.CliOutputEnvelope<McpServeExitReport>.Success(data).ToJsonString();
        TerminalHelper.WriteLine($"{TerminalColors.Info}mcp_serve 退出报告{AnsiStyleEnumConstants.Reset}: {report}");
    }

    internal static async Task<int> WithHostAsync(
        Func<IServiceProvider, Task<int>> action, string? vendor = null, string? model = null,
        CancellationToken ct = default, bool skipToolFilter = false, Func<string, bool>? toolFilter = null,
        string? requiredTool = null) {
        // await using 确保 DisposeAsync 级联释放所有服务(IKvStore → PithosKvStore → PithosDb flush MemTable),
        // 使跨进程缓存命中(第一次 MISS → flush → 第二次 HIT)。
        // 历史注释"host.Dispose() 调 Environment.Exit(210)"已过时:退出码 210 在代码中不存在,
        // 且 ExecuteServeAsync 已用 await using var appHost 验证 DisposeAsync 安全。
        var swBuild = PerfTimer.Start("BuildHostAsync");
        await using var host = await BuildHostAsync(vendor, model, ct, skipToolFilter, toolFilter).ConfigureAwait(false);
        swBuild?.Stop();
        // 单次命令等待插件后台链完成：八插件加载后台化后 registry 同步可见仅 15 个工具（gh_pr_* 全缺失），
        // gh/mcp_call/mcp_list 查询前等 Completion 补齐工具表（长驻宿主不等待，保留启动优化收益）
        // requiredTool 非空时先看编译期注册表里有没有：有就不等插件（1.6s 的 MCP 客户端握手与本次调用无关），
        // 没有才等（如 gh_* 来自插件、tool_search 要全量目录）。
        var swBoot = PerfTimer.Start("bootstrap.Completion");
        var bootstrap = host.Services.GetService<JoinCode.App.Modules.McpPluginBootstrapHandle>();
        if (bootstrap is not null && !await IsToolAlreadyRegisteredAsync(host.Services, requiredTool, ct).ConfigureAwait(false)) {
            await bootstrap.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        swBoot?.Stop();
        return await action(host.Services).ConfigureAwait(false);
    }

    /// <summary>工具是否已在注册表里（触发一次按需注册，不等待插件后台链）。</summary>
    /// <param name="services">宿主服务。</param>
    /// <param name="toolName">工具名；null 表示调用方需要全量工具表，直接返回 false 让它等插件。</param>
    /// <param name="ct">取消令牌。</param>
    private static async Task<bool> IsToolAlreadyRegisteredAsync(IServiceProvider services, string? toolName, CancellationToken ct) {
        if (string.IsNullOrEmpty(toolName)) return false;
        try {
            return await services.GetRequiredService<IMcpToolRegistry>().ContainsToolAsync(toolName, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            // 注册表初始化失败不该把「等插件」这条更保险的路也堵死 —— 退化到等待后由 action 自己报错
            System.Diagnostics.Debug.WriteLine($"[McpCliCommand] 预查工具 {toolName} 失败: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<Dictionary<string, JsonElement>?> ParseArgsAsync(
        string? args, string[]? kvArgs, Dictionary<string, string>? flagArgs, string? argsFile, bool argsStdin) {
        // 优先级: flagArgs (--参数 值) > kvArgs (key=value) > argsJson (JSON) > argsFile > argsStdin
        if (flagArgs is { Count: > 0 } || kvArgs is { Length: > 0 }) {
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in flagArgs ?? new Dictionary<string, string>())
                dict[key] = await ParseValueToJsonElementAsync(value, key).ConfigureAwait(false);
            foreach (var kv in kvArgs ?? []) {
                var eqIdx = kv.IndexOf('=');
                if (eqIdx <= 0 || eqIdx == kv.Length - 1) {
                    var detail = eqIdx <= 0 ? "缺少 '=' 分隔符" : "'=' 后面不能为空";
                    TerminalHelper.WriteError(CliErrorCatalog.ArgInvalidKeyValueFormat(kv, detail).ToRustStyleString(kv));
                    return null;
                }
                var key = kv[..eqIdx];
                var value = kv[(eqIdx + 1)..];
                dict[key] = await ParseValueToJsonElementAsync(value, key).ConfigureAwait(false);
            }
            return dict;
        }

        string? json = null;
        if (argsStdin) {
            await using var stream = System.Console.OpenStandardInput();
            await using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            var offset = 0;
            while (bytes.Length - offset >= 3 && bytes[offset] == 0xEF && bytes[offset + 1] == 0xBB && bytes[offset + 2] == 0xBF)
                offset += 3;
            json = System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        } else if (!string.IsNullOrEmpty(argsFile))
            json = System.IO.File.ReadAllText(argsFile);
        else if (!string.IsNullOrEmpty(args))
            json = args;

        if (string.IsNullOrEmpty(json))
            return new();

        try {
            return ParseJsonObject(json);
        } catch (System.Text.Json.JsonException ex) {
            var repairResult = LlmJsonHelper.RepairJson(json);
            if (repairResult.Success) {
                try {
                    var repairedDict = ParseJsonObject(repairResult.RepairedJson);
                    if (repairResult.RepairHint is not null)
                        TerminalHelper.WriteError($"JSON 参数已自动修复: {repairResult.RepairHint}");
                    return repairedDict;
                } catch (System.Text.Json.JsonException repairEx) {
                    TerminalHelper.WriteError(FormatJsonError(repairEx, repairResult.RepairedJson, null, "JSON 修复后仍解析失败"));
                }
            } else {
                TerminalHelper.WriteError(FormatJsonError(ex, json, null, "JSON 解析失败"));
                if (repairResult.RepairHint is not null)
                    TerminalHelper.WriteError($"修复提示: {repairResult.RepairHint}");
                var shellHint = LlmJsonHelper.BuildShellQuoteHint(json);
                if (shellHint is not null)
                    TerminalHelper.WriteError(shellHint);
            }
            return null;
        }
    }

    /// <summary>
    /// 将 JSON 字符串解析为 Dictionary（必须是 JSON 对象）
    /// </summary>
    private static Dictionary<string, JsonElement> ParseJsonObject(string json) {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) {
            TerminalHelper.WriteError("参数 JSON 必须是对象（{}），不能是数组或标量");
            throw new System.Text.Json.JsonException("JSON 必须是对象");
        }
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    /// <summary>
    /// 格式化 JSON 解析错误 — Rust 风格，箭头指向出错位置
    /// </summary>
    private static string FormatJsonError(System.Text.Json.JsonException ex, string json, string? keyName, string title) {
        var sb = new StringBuilder();
        sb.Append(title);
        if (keyName is not null)
            sb.Append($" (参数: {keyName})");
        var msg = ex.Message;
        var posIdx = msg.IndexOf("LineNumber:");
        if (posIdx > 0)
            msg = msg[..(posIdx - 1)].TrimEnd('.');
        sb.Append($": {msg}");

        var lineNum = ex.LineNumber >= 0 ? (int)ex.LineNumber : 0;
        var colNum = ex.BytePositionInLine >= 0 ? (int)ex.BytePositionInLine : 0;

        var lines = json.Split('\n');
        var errorLine = lineNum < lines.Length ? lines[lineNum].TrimEnd('\r') : "";
        var displayLine = lineNum + 1;
        var displayCol = colNum + 1;
        var lineLabel = displayLine.ToString();

        sb.Append($"\n  --> 行 {displayLine}, 列 {displayCol}");
        sb.Append("\n   |");

        if (errorLine.Length > 0) {
            sb.Append($"\n {lineLabel} | {errorLine}");
            var markerCol = Math.Min(colNum, errorLine.Length);
            var markerIndent = new string(' ', markerCol);
            sb.Append($"\n   | {markerIndent}^");
        }

        var escaped = json.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        sb.Append($"\n  原始输入: {escaped}");

        return sb.ToString();
    }

    /// <summary>
    /// 将 key=value 的字符串值转换为 JsonElement（支持 int/double/bool/string/JSON对象/JSON数组）。
    /// <para>当 value 以 { 或 [ 开头时，尝试解析为 JSON 对象或数组；解析失败时调用 LlmJsonHelper.RepairJson 修复（处理 PowerShell 引号剥离等问题）。</para>
    /// </summary>
    /// <summary>将字符串值按类型推断转换为 JsonElement（int/double/bool/null/JSON/字符串）</summary>
    internal static async Task<JsonElement> ParseValueToJsonElementAsync(string value, string keyName) {
        if (value.Length > 0 && (value[0] == '{' || value[0] == '[')) {
            try {
                return JsonDocument.Parse(value).RootElement.Clone();
            } catch (System.Text.Json.JsonException ex) {
                var repairResult = LlmJsonHelper.RepairJson(value);
                if (repairResult.Success) {
                    try {
                        return JsonDocument.Parse(repairResult.RepairedJson).RootElement.Clone();
                    } catch (System.Text.Json.JsonException repairEx) {
                        TerminalHelper.WriteError(FormatJsonError(repairEx, repairResult.RepairedJson, keyName, "JSON value 修复后仍解析失败"));
                        TerminalHelper.WriteError($"  修复后: {repairResult.RepairedJson}");
                    }
                } else {
                    ReportJsonParseFailure(ex, value, keyName, repairResult.RepairHint);
                }
            }
        }

        if (int.TryParse(value, out var intVal))
            return JsonDocument.Parse(intVal.ToString()).RootElement.Clone();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
            return JsonDocument.Parse(doubleVal.ToString(CultureInfo.InvariantCulture)).RootElement.Clone();
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("true").RootElement.Clone();
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("false").RootElement.Clone();
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return JsonDocument.Parse("null").RootElement.Clone();
        // 字符串值 — 用 Utf8JsonWriter 写入（AOT 兼容）
        await using var ms = new System.IO.MemoryStream();
        await using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            writer.WriteStringValue(value);
        ms.Position = 0;
        return JsonDocument.Parse(ms).RootElement.Clone();
    }

    /// <summary>输出工具执行结果 — 供 gh 等子命令复用，避免第二套输出逻辑
    /// <para>扁平化: 单 text content + 非 error → data 直接是 string,消除 content[0].text 嵌套(降低 AI token 消耗)</para>
    /// <para>计时: ToolResult.TimingInfo → CliOutputMeta.Timing (JSON) / 末行输出 (非JSON)</para>
    /// </summary>
    internal static int OutputResult(ToolResult result, bool json) {
        var meta = result.TimingInfo is not null
            ? new Cli.Output.CliOutputMeta { Timing = result.TimingInfo }
            : null;
        if (json) {
            OutputJsonResult(result, meta);
        } else {
            // 非 json 模式: 遍历所有 Content,输出文本 + 图片摘要
            var hasOutput = false;
            foreach (var c in result.Content) {
                if (!string.IsNullOrEmpty(c.Text)) {
                    WriteTextOrError(c.Text, result.IsError);
                    hasOutput = true;
                } else if (!string.IsNullOrEmpty(c.Data)) {
                    // 图片内容: 输出摘要信息(base64 太长不直接输出到控制台)
                    var mimeType = c.MimeType ?? "unknown";
                    var decodedSize = c.Data.Length * 3 / 4;
                    TerminalHelper.WriteLine($"[图片: {mimeType}, {c.Data.Length} 字节 base64 ≈ {decodedSize} 字节]");
                    hasOutput = true;
                }
            }
            if (!hasOutput)
                WriteTextOrError("(无文本输出)", result.IsError);
            if (result.TimingInfo is not null)
                TerminalHelper.WriteLine(result.TimingInfo);
        }
        return result.IsError ? 1 : 0;
    }

    /// <summary>
    /// JSON 模式输出 — 单 text content 直接作为 data，多 content 用完整 ToolResult
    /// <para>如果 text 是有效 JSON（以 [ 或 { 开头），输出结构化 data 而非字符串（避免双重编码）</para>
    /// </summary>
    private static void OutputJsonResult(JoinCode.Abstractions.Tools.ToolResult result, Cli.Output.CliOutputMeta? meta) {
        var text = (result.Content.Count == 1 ? result.Content[0].Text : null) ?? string.Empty;
        if (result.IsError || string.IsNullOrEmpty(text)) {
            var envelope = Cli.Output.CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolResult>.Success(result, meta);
            System.Console.WriteLine(envelope.ToJsonString());
            return;
        }
        if ((text[0] == '[' || text[0] == '{') && TryOutputStructuredJson(text, meta))
            return;
        var strEnvelope = Cli.Output.CliOutputEnvelope<string>.Success(text.Replace("\r\n", "\n"), meta);
        System.Console.WriteLine(strEnvelope.ToJsonString());
    }

    /// <summary>
    /// 尝试将 text 作为结构化 JSON 输出到 stdout — 解析为 JsonElement 后用 CliOutputEnvelope&lt;JsonElement&gt; 输出
    /// <para>避免 JSON 字符串被双重编码：text="[1,2,3]" → data:[1,2,3] 而非 data:"[1,2,3]"</para>
    /// </summary>
    /// <param name="text">可能是 JSON 的文本</param>
    /// <param name="meta">可选元数据</param>
    /// <returns>text 是有效 JSON 且已输出返回 true，否则返回 false</returns>
    private static bool TryOutputStructuredJson(string text, Cli.Output.CliOutputMeta? meta) {
        try {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var element = doc.RootElement.Clone();
            var envelope = Cli.Output.CliOutputEnvelope<System.Text.Json.JsonElement>.Success(element, meta);
            System.Console.WriteLine(envelope.ToJsonString());
            return true;
        } catch {
            return false;
        }
    }

    private static int OutputError(string message, bool json) {
        if (json) {
            var envelope = CliOutputEnvelope.Fail(new CliStructuredError("TOOL_ERROR", message, null, false));
            System.Console.WriteLine(envelope.ToString());
        } else {
            TerminalHelper.WriteError(message);
        }
        return 1;
    }

    private static void AppendEscapedJson(StringBuilder sb, string text) {
        foreach (var c in text) {
            switch (c) {
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

    private static void ReportJsonParseFailure(System.Text.Json.JsonException ex, string value, string keyName, string? repairHint) {
        TerminalHelper.WriteError(FormatJsonError(ex, value, keyName, "JSON value 解析失败"));
        if (repairHint is not null)
            TerminalHelper.WriteError($"  修复提示: {repairHint}");
    }

    private static void WriteTextOrError(string text, bool isError) {
        if (isError)
            TerminalHelper.WriteError(text);
        else
            TerminalHelper.WriteLine(text);
    }
}

/// <summary>mcp_serve 退出报告 DTO — 用泛型 CliOutputEnvelope&lt;T&gt; 序列化，AOT 兼容</summary>
public sealed record McpServeExitReport(string Transport, int ToolCount, int TotalRequests, int TotalErrors, double UptimeSeconds);
