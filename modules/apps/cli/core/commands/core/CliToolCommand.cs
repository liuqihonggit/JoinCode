namespace JoinCode.CliCommands;

/// <summary>
/// CLI 层工具节点发现 + 进程隔离执行入口 — ADR 0157 v3 第 4 点。
/// 目录树 + 关键词搜索下钻，取代 MCP 的 tool_groups / 三级 map[] 语法。
/// 结果只打印到 stdout，LLM 用 bash 按需读取，查完即弃，永不注入系统提示词。
/// <para>call 动作是「进程隔离」的执行面：jcc 主进程不加载任何工具实现 / 引擎 DI，
/// 只通过 Process.Start 启动对应分组 exe（jcc-tool-&lt;组&gt;.exe），透传参数并回传 stdout/stderr/退出码。
/// 工具实现运行在独立进程里，与宿主进程隔离 — 这正是 ADR 0157 把工具拆成 exe 的目的。</para>
/// </summary>
internal static class CliToolCommand {
    private const string ToolsDirEnvVar = "JCC_CLI_TOOLS_DIR";
    private const string ExeDirEnvVar = "JCC_TOOL_EXE_DIR";

    /// <summary>执行 jcc tool 元命令的动作分发。</summary>
    /// <param name="args">完整命令行参数，args[0] 为 tool，args[1] 为动作。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var action = args.Length > 1 ? args[1] : "tree";
        switch (action) {
            case "tree": return await RunTreeAsync(args, ct).ConfigureAwait(false);
            case "find": return await RunFindAsync(args, ct).ConfigureAwait(false);
            case "base": return RunBase();
            case "help": return await RunHelpAsync(args, ct).ConfigureAwait(false);
            case "call": return await RunCallAsync(args, ct).ConfigureAwait(false);
            case "-h":
            case "--help": return WriteUsage();
            default: return WriteUnknownAction(action);
        }
    }

    private static async Task<int> RunTreeAsync(string[] args, CancellationToken ct) {
        var dir = ResolveToolsDir(args);
        var all = NodeManifestLoader.LoadNodesFromDirectory(dir);
        var enabled = FilterByProfile(all, out var disabled);
        var scope = await CliHostToolAuthorization.GetScopeAsync(ct).ConfigureAwait(false);
        var nodes = CliToolScopeFilter.Apply(enabled, scope);
        Console.Out.WriteLine($"CLI 工具节点目录: {dir}");
        if (all.Count == 0) {
            Console.Out.WriteLine("  (未发现 node.yaml — 用 --dir 指定工具目录，或设置 JCC_CLI_TOOLS_DIR)");
        } else if (enabled.Count == 0) {
            Console.Out.WriteLine("  (当前 profile 未启用任何工具组 —— 检查 profile 的 tool-groups 消费点)");
        } else if (nodes.Count == 0) {
            Console.Out.WriteLine("  (当前 agent 的工具域内没有可发现的 CLI 工具 —— 未授权工具不会出现在目录里)");
        } else {
            Console.Out.WriteLine(CliToolSearch.RenderTree(nodes));
        }
        if (scope is not null) WriteScopeTrimSummary();
        WriteProfileTrimSummary(disabled);
        return 0;
    }

    private static async Task<int> RunFindAsync(string[] args, CancellationToken ct) {
        var query = ExtractQuery(args);
        if (string.IsNullOrWhiteSpace(query)) {
            Console.Error.WriteLine("缺少查询关键词（由 jcc tool find 的参数触发，CliToolCommand 参数校验层拦截）。");
            Console.Error.WriteLine("正确写法: jcc tool find pr check --dir <工具目录>");
            return 2;
        }
        var dir = ResolveToolsDir(args);
        var enabled = FilterByProfile(NodeManifestLoader.LoadNodesFromDirectory(dir), out var disabled);
        var scope = await CliHostToolAuthorization.GetScopeAsync(ct).ConfigureAwait(false);
        var nodes = CliToolScopeFilter.Apply(enabled, scope);
        var matches = CliToolSearch.Find(nodes, query);
        Console.Out.WriteLine($"查询: {query}  |  目录: {dir}");
        Console.Out.WriteLine(CliToolSearch.RenderResults(matches, query));
        // 搜不到时最容易误判成「工具不存在」—— 被 profile / 工具域裁掉的组必须先说清楚。
        if (scope is not null) WriteScopeTrimSummary();
        WriteProfileTrimSummary(disabled);
        return 0;
    }

    private static int RunBase() {
        var registry = BaseToolRegistry.LoadDefault();
        Console.Out.WriteLine($"基础工具（来自 base.yaml，共 {registry.Count} 个 — 首轮 tools 字段唯一来源）:");
        foreach (var tool in registry.All)
            Console.Out.WriteLine($"  {tool.Id,-18} {tool.Name,-16} {tool.Summary}");
        return 0;
    }

    private static async Task<int> RunHelpAsync(string[] args, CancellationToken ct) {
        var id = args.Length > 2 ? args[2] : null;
        if (string.IsNullOrWhiteSpace(id)) return WriteUsage();
        var all = NodeManifestLoader.LoadNodesFromDirectory(ResolveToolsDir(args));
        var enabled = FilterByProfile(all, out _);
        var scope = await CliHostToolAuthorization.GetScopeAsync(ct).ConfigureAwait(false);
        var node = CliToolScopeFilter.Apply(enabled, scope)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (node is null) {
            // 未授权 ≠ 未启用 ≠ 未知：三种情况给三种说法，否则用户会去改错地方。
            if (enabled.Any(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)))
                return WriteGroupOutsideScope(id);
            if (all.Any(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)))
                return WriteGroupDisabled(id);
            Console.Error.WriteLine($"未找到节点 '{id}'（由 jcc tool help 的参数触发，CliToolCommand 节点查找层拦截）。");
            Console.Error.WriteLine("正确写法: 先用 jcc tool find <关键词> 找到节点 ID，再 jcc tool help <ID>。");
            return 1;
        }
        Console.Out.WriteLine(CliToolHelp.Render(node));
        if (scope is not null) WriteScopeTrimSummary();
        return 0;
    }

    /// <summary>工具域裁剪提示 — 让调用方把「搜不到/看不到」正确归因到未授权，而不是断言工具不存在。</summary>
    private static void WriteScopeTrimSummary() {
        Console.Out.WriteLine("  (已按当前 agent 工具域裁剪：未授权工具不会出现在目录里，执行时同样会被拒绝)");
    }

    private static int WriteGroupOutsideScope(string id) {
        Console.Error.WriteLine($"分组 '{id}' 的工具不在当前 agent 的工具域内（由 jcc tool help 的工具域裁剪层拦截）。");
        Console.Error.WriteLine("正确写法: 调用当前职责范围内的工具；需要该能力时交由具备对应职责的子代理执行。");
        return 1;
    }

    /// <summary>
    /// 按 profile 的 <c>tool-groups</c> 消费点过滤组；被裁掉的组 ID 通过 <paramref name="disabled"/> 回传。
    /// <para>过滤与诊断必须用同一份结果：分别算一次，就会出现「说裁了 2 组、实际少了 3 组」。</para>
    /// </summary>
    /// <param name="nodes">全量组节点。</param>
    /// <param name="disabled">被 profile 裁掉的组 ID。</param>
    /// <returns>启用的组节点。</returns>
    private static IReadOnlyList<CliToolDescriptor> FilterByProfile(
        IReadOnlyList<CliToolDescriptor> nodes,
        out IReadOnlyList<string> disabled) {
        var enabled = new List<CliToolDescriptor>(nodes.Count);
        var off = new List<string>();
        foreach (var node in nodes) {
            if (CliToolGate.IsEnabled(node.Id))
                enabled.Add(node);
            else
                off.Add(node.Id);
        }
        disabled = off;
        return enabled;
    }

    /// <summary>被 profile 裁剪时补一行诊断 —— 「工具少了」的原因要能当场看到，而不是靠猜。</summary>
    /// <param name="disabled">被裁掉的组 ID。</param>
    private static void WriteProfileTrimSummary(IReadOnlyList<string> disabled) {
        if (disabled.Count == 0) return;
        Console.Out.WriteLine(
            $"[TOOL-GROUP] profile={CliProfileGate.ProfileName} 未启用 {disabled.Count} 组: {string.Join(",", disabled)}"
            + "（消费点 tool-groups；在 profile 里加回该组即恢复）");
    }

    /// <summary>组被 profile 裁掉时的统一措辞（<c>help</c> / <c>call</c> 共用，避免两处措辞漂移）。</summary>
    /// <param name="group">组 ID。</param>
    /// <returns>退出码 2（配置错误），与「未找到工具」的 2 保持同码但措辞不同。</returns>
    private static int WriteGroupDisabled(string group) {
        Console.Error.WriteLine($"工具组 '{group}' 未在当前 profile 启用（profile={CliProfileGate.ProfileName}，消费点=tool-groups，节点={group}）。");
        Console.Error.WriteLine($"恢复用法: 在 profile 的 tool-groups 里加回 '- id: {group}'。");
        return 2;
    }

    /// <summary>
    /// 进程隔离执行 — jcc 启动分组 exe 运行工具（ADR 0157 第 4 点）。
    /// <para>本方法只做四件事：解析目标（分组/工具/别名）、定位 exe、启动进程、回传 stdout/stderr/退出码。
    /// 不初始化引擎 DI、不加载任何工具实现，工具逻辑全在被启动的独立进程里运行。</para>
    /// <para>用法: jcc tool call [&lt;分组&gt;] &lt;工具名&gt; [--参数 值 ...] [--exe &lt;path&gt;] [--timeout &lt;秒&gt;]</para>
    /// <para>分组可省略：给工具名即可由 node.yaml 的子工具清单反查所属分组，少一次往返。</para>
    /// </summary>
    private static async Task<int> RunCallAsync(string[] args, CancellationToken ct) {
        var call = ParseCallArguments(args);
        if (call.Names.Count == 0) return WriteCallUsage();

        var nodes = NodeManifestLoader.LoadNodesFromDirectory(call.ToolsDir ?? ResolveToolsDir(args));
        var target = TryResolveTarget(call, nodes, out var failureExitCode);
        if (target is null) return failureExitCode;

        // 用全量节点解析目标（这样「未知工具」与「未启用组」是两种准确错误），
        // 再对解析出的组判门禁 —— 反过来先过滤会让被裁组报成「未找到工具」，指向错误的方向。
        if (!CliToolGate.IsEnabled(target.Group))
            return WriteGroupDisabled(target.Group);

        // 用户口径（2026-11-16）：工具能不能独立跑是 exe 的事；「Agent 自己的功能」（配置、记忆、
        // 权限、会话等依赖宿主状态的 host 工具）就该留在 jcc 内。原先给这类工具也先起一个分组 exe、
        // 再由它起 jcc mcp_call，白白多两层进程；mode: host 时直接在当前 jcc 进程执行同一条 mcp_call 路径。
        // local / delegate 不变 —— 它们本来就是「独立 exe 就是工具」。
        // 例外：斜杠命令组的 host 工具走宿主 slash_call 而非 mcp_call（它们不是 MCP 工具），
        // 不能走进程内 mcp_call 快路径，必须让分组 exe 用 HostVerb=slash_call 转发。
        var groupNode = nodes.FirstOrDefault(node => string.Equals(node.Id, target.Group, StringComparison.OrdinalIgnoreCase));
        var isSlashGroup = groupNode?.Exe?.Contains("slash", StringComparison.OrdinalIgnoreCase) == true
            || target.Group.Equals("slash", StringComparison.OrdinalIgnoreCase);
        if (call.ExplicitExe is null && !isSlashGroup && groupNode?.FindTool(target.Tool)?.Mode == CliExecutionMode.Host)
            return await RunHostToolInProcessAsync(target.Tool, call, ct).ConfigureAwait(false);

        var exe = call.ExplicitExe ?? ResolveGroupExe(target.Group);
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return WriteExeMissing(target.Group, exe);

        return await LaunchAsync(exe, target, call, ResolveAuthorizationEnvironment(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析本次子进程的授权环境：agent shell 已注入则沿用它；手工调用则在当前 jcc 进程启动 broker。
    /// <para>分组 exe 只接受 broker 会话，直接运行无法构造有效令牌。</para>
    /// </summary>
    private static IReadOnlyDictionary<string, string> ResolveAuthorizationEnvironment() {
        var pipe = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable);
        var token = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable);
        if (!string.IsNullOrWhiteSpace(pipe) && !string.IsNullOrWhiteSpace(token)) {
            return new Dictionary<string, string>(StringComparer.Ordinal) {
                [CliAuthorizationEnvironment.PipeVariable] = pipe,
                [CliAuthorizationEnvironment.TokenVariable] = token
            };
        }

        return CliAuthorizationBrokerRuntime.EnsureEnvironment(
            agentId: "jcc-manual",
            sessionId: "jcc-tool-call",
            allowedTools: [],
            deniedTools: [],
            permissionMode: null);
    }

    /// <summary>
    /// 在 jcc 进程内执行 host 工具 — 复用 <see cref="McpCliCommand"/> 的权限管道与输出格式，
    /// 不启动分组 exe（它对该工具只有转发职责）。保留 <c>--timeout</c> 语义：超时返回 124。
    /// <para>参数以字典直传，不走 argv 往返：工具参数名不会被元命令选项（--json/--model 等）
    /// 抢走，值也不会因 shell 转义而变形。</para>
    /// </summary>
    private static async Task<int> RunHostToolInProcessAsync(string toolId, CallArguments call, CancellationToken ct) {
        var arguments = ExtractToolArguments(call.PassThrough);
        if (call.TimeoutSeconds <= 0)
            return await McpCliCommand.ExecuteCallWithArgumentsAsync(toolId, arguments, json: true, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(call.TimeoutSeconds));
        try {
            return await McpCliCommand.ExecuteCallWithArgumentsAsync(toolId, arguments, json: true, timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested) {
            Console.Error.WriteLine($"工具 '{toolId}' 执行超时（--timeout {call.TimeoutSeconds} 秒）。");
            return 124;
        }
    }

    /// <summary>
    /// 把透传的 <c>--参数 值</c> 片段解析成工具参数表。
    /// <para>此处没有元命令选项要保留，透传片段里的 <c>--xxx</c> 全是工具参数；
    /// 裸值不是工具参数（工具参数一律带 <c>--</c>），直接忽略。</para>
    /// </summary>
    private static Dictionary<string, string> ExtractToolArguments(IReadOnlyList<string> passThrough) {
        if (passThrough.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        var (flagArgs, _) = McpToolArgumentBinder.ExtractFlagArguments(
            passThrough.ToArray(), startIndex: 0,
            reservedOptions: FrozenSet<string>.Empty,
            reservedBooleanFlags: FrozenSet<string>.Empty);
        return flagArgs;
    }

    /// <summary>call 动作的解析结果 — 名称、执行器自有选项、透传给工具的其余参数。</summary>
    private sealed record CallArguments(
        IReadOnlyList<string> Names,
        IReadOnlyList<string> PassThrough,
        string? ExplicitExe,
        string? ToolsDir,
        int TimeoutSeconds);

    /// <summary>已确认的执行目标：分组 ID 与规范化后的工具 ID（别名已换成规范 ID）。</summary>
    private sealed record CallTarget(string Group, string Tool);

    /// <summary>
    /// 解析 call 参数。名称只取开头的连续 token，遇到第一个 executor 自有选项或已知名称之外的 token 即停，
    /// 其后全部原样透传给工具（工具自己的 <c>--参数</c> 不在这里解释）。
    /// </summary>
    private static CallArguments ParseCallArguments(string[] args) {
        const int defaultTimeoutSeconds = 180;
        var names = new List<string>();
        var passThrough = new List<string>();
        string? explicitExe = null;
        string? toolsDir = null;
        var timeoutSeconds = defaultTimeoutSeconds;
        var namesClosed = false;

        for (var i = 2; i < args.Length; i++) {
            var token = args[i];
            if (token == "--exe" && i + 1 < args.Length) {
                explicitExe = args[++i];
                namesClosed = true;
                continue;
            }
            // --dir 属于执行器（影响本次 node.yaml 检索），不能透传给工具，否则工具会因未知参数报错。
            if (token == "--dir" && i + 1 < args.Length) {
                toolsDir = args[++i];
                namesClosed = true;
                continue;
            }
            if (token == "--timeout" && i + 1 < args.Length) {
                if (!int.TryParse(args[i + 1], out timeoutSeconds) || timeoutSeconds < 0) {
                    Console.Error.WriteLine($"--timeout 的值 '{args[i + 1]}' 无效（由 jcc tool call 参数校验层拦截）。");
                    Console.Error.WriteLine("正确写法: --timeout 180（秒）；0 表示不设超时。");
                    timeoutSeconds = defaultTimeoutSeconds;
                }
                i++;
                namesClosed = true;
                continue;
            }
            // 名称阶段：只在遇到第一个选项（-h / --参数）之前收集，最多两个（分组、工具）。
            // 一旦关闭，后面所有裸值都属于工具——否则 `file_read --file_path X` 会把路径当成分组名。
            if (!namesClosed && names.Count < 2 && !token.StartsWith('-')) {
                names.Add(token);
                continue;
            }
            namesClosed = true;
            passThrough.Add(token);
        }
        return new CallArguments(names, passThrough, explicitExe, toolsDir, timeoutSeconds);
    }

    /// <summary>
    /// 把「名称 + 子工具清单」解析成确定的分组与工具 ID。
    /// <para>分组名和工具名都先经 <see cref="CliArgNormalizer.NormalizeNodeName"/> 归一化，
    /// 这样 Git Bash 把 <c>/agents</c> 展开成 <c>&lt;Git安装根&gt;/agents</c> 时仍能命中。</para>
    /// <para>工具名支持别名：命中别名时换成规范 ID 传给 exe，否则 exe 会认为工具不存在。</para>
    /// </summary>
    /// <returns>解析成功返回执行目标；失败返回 <see langword="null"/>，并已写出带正确写法的引导与退出码。</returns>
    private static CallTarget? TryResolveTarget(
        CallArguments call,
        IReadOnlyList<CliToolDescriptor> nodes,
        out int failureExitCode) {
        var firstName = CliArgNormalizer.NormalizeNodeName(call.Names[0]);
        return call.Names.Count > 1
            ? ResolveByGroupAndTool(nodes, firstName, CliArgNormalizer.NormalizeNodeName(call.Names[1]), out failureExitCode)
            : ResolveByToolName(nodes, firstName, out failureExitCode);
    }

    /// <summary>形式一：<c>&lt;分组&gt; &lt;工具&gt;</c>；顺序记错成 <c>&lt;工具&gt; &lt;分组&gt;</c> 时也接受。</summary>
    private static CallTarget? ResolveByGroupAndTool(
        IReadOnlyList<CliToolDescriptor> nodes,
        string groupName,
        string toolName,
        out int failureExitCode) {
        failureExitCode = 0;
        var node = FindNode(nodes, groupName);
        if (node is not null) return TryBindTool(node, toolName, out failureExitCode);

        // 允许「工具名 分组」颠倒写法：两者都试，避免 LLM 记错顺序就失败。
        var swappedNode = FindNode(nodes, toolName);
        var swappedEntry = swappedNode?.FindTool(groupName);
        if (swappedNode is not null && swappedEntry is not null)
            return new CallTarget(swappedNode.Id, swappedEntry.Id);

        failureExitCode = WriteUnknownGroup(groupName, nodes);
        return null;
    }

    /// <summary>
    /// 形式二：只给工具名 → 反查所属分组。
    /// 一次收集「分组 + 条目」成对结果，避免二次查找再补空引用断言。
    /// </summary>
    private static CallTarget? ResolveByToolName(
        IReadOnlyList<CliToolDescriptor> nodes,
        string toolName,
        out int failureExitCode) {
        failureExitCode = 0;
        var owners = new List<(string Group, CliToolEntry Entry)>();
        foreach (var node in nodes) {
            var entry = node.FindTool(toolName);
            if (entry is not null) owners.Add((node.Id, entry));
        }
        if (owners.Count == 1) return new CallTarget(owners[0].Group, owners[0].Entry.Id);
        if (owners.Count > 1) {
            Console.Error.WriteLine($"工具名 '{toolName}' 同时出现在多个分组（由 jcc tool call 的分组反查层拦截）：{string.Join(", ", owners.Select(owner => owner.Group))}");
            Console.Error.WriteLine("正确写法: jcc tool call <分组> <工具名> —— 显式指定分组消除歧义。");
            failureExitCode = 2;
            return null;
        }

        // 名称可能是分组本身（想列出该组工具）
        var directNode = FindNode(nodes, toolName);
        if (directNode is not null) {
            Console.Error.WriteLine($"'{toolName}' 是分组名，缺少工具名（由 jcc tool call 的参数校验层拦截）。");
            Console.Error.WriteLine($"该分组共 {directNode.Tools.Length} 个工具，用 jcc tool help {directNode.Id} 查看全部。");
            Console.Error.WriteLine($"正确写法: jcc tool call {directNode.Id} <工具名> [--参数 值]");
            failureExitCode = 2;
            return null;
        }

        failureExitCode = WriteUnknownTool(toolName, nodes);
        return null;
    }

    /// <summary>在分组节点内绑定工具名（支持别名 → 规范 ID）；未知工具给相近建议而非静默转发。</summary>
    private static CallTarget? TryBindTool(CliToolDescriptor node, string toolName, out int failureExitCode) {
        failureExitCode = 0;
        var entry = node.FindTool(toolName);
        if (entry is not null) return new CallTarget(node.Id, entry.Id);
        // 无子工具清单时（未生成 node.yaml 的旧目录）不做拦截，交给 exe 自己判断。
        if (!node.HasToolEntries) return new CallTarget(node.Id, toolName);
        var close = node.Tools
            .Where(candidate => candidate.Id.Contains(toolName, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Id)
            .Take(8)
            .ToArray();
        Console.Error.WriteLine($"分组 '{node.Id}' 内没有工具 '{toolName}'（由 jcc tool call 的工具校验层拦截）。");
        Console.Error.WriteLine($"该分组共 {node.Tools.Length} 个工具，进程隔离 {node.LocalToolCount} 个（local 本 exe 内实现 + delegate 独立 exe）。");
        if (close.Length > 0) Console.Error.WriteLine($"相近工具: {string.Join(", ", close)}");
        Console.Error.WriteLine($"正确写法: jcc tool help {node.Id} 查看全部工具，或 jcc tool find <关键词> 搜索。");
        failureExitCode = 2;
        return null;
    }

    private static CliToolDescriptor? FindNode(IReadOnlyList<CliToolDescriptor> nodes, string name) =>
        nodes.FirstOrDefault(node => string.Equals(node.Id, name, StringComparison.OrdinalIgnoreCase));

    private static int WriteUnknownTool(string toolName, IReadOnlyList<CliToolDescriptor> nodes) {
        var groups = string.Join(", ", nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal));
        Console.Error.WriteLine($"未找到工具 '{toolName}'（由 jcc tool call 的分组反查层拦截）。");
        Console.Error.WriteLine($"当前工具目录的可用分组: {groups}");
        Console.Error.WriteLine("正确写法: jcc tool find <关键词> 先搜索，再 jcc tool call <分组> <工具名> [--参数 值]。");
        return 2;
    }

    private static int WriteUnknownGroup(string group, IReadOnlyList<CliToolDescriptor> nodes) {
        var groups = string.Join(", ", nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal));
        Console.Error.WriteLine($"未找到分组 '{group}'（由 jcc tool call 的分组解析层拦截）。");
        Console.Error.WriteLine($"可用分组: {(groups.Length == 0 ? "(未加载到任何 node.yaml，请用 --dir 指定工具目录)" : groups)}");
        Console.Error.WriteLine("正确写法: jcc tool call <分组> <工具名> [--参数 值]；或省略分组只给工具名自动反查。");
        return 2;
    }

    private static int WriteExeMissing(string group, string? exe) {
        Console.Error.WriteLine($"未找到分组 '{group}' 的独立 exe（由 jcc tool call 的 exe 定位层拦截）：{exe ?? "(未解析出路径)"}");
        Console.Error.WriteLine("原因: 该分组 exe 尚未编译，或不在默认查找路径。");
        Console.Error.WriteLine("正确写法: 先 dotnet build 该分组 csproj，或用 --exe <path> 显式指定 exe 路径。");
        Console.Error.WriteLine($"查找顺序: --exe > {ExeDirEnvVar} > {ToolsDirEnvVar}/<组> > <jcc目录>/tools/<组> > 仓库 artifacts/bin");
        return 3;
    }

    private static int WriteCallUsage() {
        Console.Error.WriteLine("缺少分组或工具名（由 jcc tool call 的参数触发，CliToolCommand 参数校验层拦截）。");
        Console.Error.WriteLine("正确写法: jcc tool call [<分组>] <工具名> [--参数 值 ...] [--timeout <秒>]");
        Console.Error.WriteLine("示例: jcc tool call file_read --file_path D:/tmp/a.txt");
        Console.Error.WriteLine("示例: jcc tool call file file_read --file_path D:/tmp/a.txt");
        Console.Error.WriteLine("可用分组见: jcc tool tree");
        return 2;
    }

    /// <summary>
    /// 启动分组 exe 并回传结果。
    /// <para>超时是必需的：工具 exe 里可能跑长命令，卡死时不设超时会把 jcc 一起拖住，
    /// 上层 agent 只能等整轮超时。超时按 GNU timeout 惯例返回 124，并杀掉整棵进程树
    /// （工具 exe 常自己再起子进程，只杀父进程会留下孤儿）。</para>
    /// </summary>
    private static async Task<int> LaunchAsync(
        string exe,
        CallTarget target,
        CallArguments call,
        IReadOnlyDictionary<string, string> authorizationEnvironment,
        CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var pair in authorizationEnvironment)
            psi.Environment[pair.Key] = pair.Value;
        // ArgumentList 逐参数传递，不手工拼命令行字符串 — 避免空格/引号/特殊字符转义问题。
        psi.ArgumentList.Add(target.Tool);
        foreach (var arg in call.PassThrough)
            psi.ArgumentList.Add(arg);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (call.TimeoutSeconds > 0) timeoutCts.CancelAfter(TimeSpan.FromSeconds(call.TimeoutSeconds));

        try {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("系统未返回新进程");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                KillTree(process);
                // 超时前工具可能已经输出了诊断信息，尽量带回来——否则用户只看到"超时"两个字。
                var partialOut = await ReadPartialAsync(stdoutTask).ConfigureAwait(false);
                var partialErr = await ReadPartialAsync(stderrTask).ConfigureAwait(false);
                if (partialOut.Length > 0) Console.Out.Write(partialOut);
                if (partialErr.Length > 0) Console.Error.Write(partialErr);
                WriteTimeout(exe, target, call);
                return TimeoutExitCode;
            }
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stdout)) Console.Out.Write(stdout);
            if (!string.IsNullOrEmpty(stderr)) Console.Error.Write(stderr);
            return process.ExitCode;
        } catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception
                                       or InvalidOperationException
                                       or OperationCanceledException) {
            Console.Error.WriteLine($"分组 exe 启动/执行失败（进程隔离层拦截）：{ex.Message}");
            Console.Error.WriteLine($"exe: {exe}");
            Console.Error.WriteLine("正确写法: 确认 exe 已编译且可执行，或用 --exe <path> 显式指定。");
            return 4;
        }
    }

    /// <summary>超时退出码 — 与 GNU timeout(1) 一致，便于上层脚本统一判断。</summary>
    private const int TimeoutExitCode = 124;

    private static void KillTree(Process process) {
        try {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        } catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception) {
            // 进程已自行退出或无法终止：不影响随后的超时报告，记录到 stderr 便于排查。
            Console.Error.WriteLine($"[jcc tool call] 超时终止进程树失败（进程可能已退出）：{ex.Message}");
        }
    }

    /// <summary>超时后尽力取回已产生的输出 — 进程已被杀，读取可能立即完成，也可能拿不到。</summary>
    private static async Task<string> ReadPartialAsync(Task<string> readTask) {
        try {
            return await readTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        } catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or InvalidOperationException) {
            return string.Empty;
        }
    }

    private static void WriteTimeout(string exe, CallTarget target, CallArguments call) {
        Console.Error.WriteLine($"工具执行超时（由 jcc tool call 的进程隔离层拦截）：{target.Group}/{target.Tool} 超过 {call.TimeoutSeconds} 秒未结束，已终止进程树。");
        Console.Error.WriteLine($"exe: {exe}");
        Console.Error.WriteLine("原因: 工具本身在等待外部输入/网络/长命令，或陷入了死循环。");
        Console.Error.WriteLine("正确写法: 缩短命令范围后重试，或用 --timeout <秒> 显式放宽；确实需要长跑请后台执行。");
    }

    /// <summary>按优先级定位分组 exe：显式目录 > 平铺目录 > 工具目录树 > 发布布局 > 仓库开发布局。</summary>
    private static string? ResolveGroupExe(string group) {
        var exeName = $"jcc-tool-{group}.exe";

        var flatDir = Environment.GetEnvironmentVariable(ExeDirEnvVar);
        if (!string.IsNullOrWhiteSpace(flatDir)) {
            var candidate = Path.Combine(flatDir, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        var toolsDir = Environment.GetEnvironmentVariable(ToolsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(toolsDir)) {
            var candidate = Path.Combine(toolsDir, group, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        var published = Path.Combine(AppContext.BaseDirectory, "tools", group, exeName);
        if (File.Exists(published)) return published;

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is not null) {
            foreach (var cfg in new[] { "Debug", "Release" }) {
                var groupBin = Path.Combine(repoRoot, "artifacts", "bin", $"jcc_tool_{group}", cfg);
                if (!Directory.Exists(groupBin)) continue;
                foreach (var tfmDir in Directory.EnumerateDirectories(groupBin)) {
                    var candidate = Path.Combine(tfmDir, exeName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>从 exe 所在目录向上找含 artifacts/bin 的仓库根目录。</summary>
    private static string? FindRepoRoot(string start) {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent) {
            if (Directory.Exists(Path.Combine(dir.FullName, "artifacts", "bin")))
                return dir.FullName;
        }
        return null;
    }

    private static int WriteUsage() {
        Console.Out.WriteLine("jcc tool — CLI 工具节点发现 + 进程隔离执行（ADR 0157 v3）");
        Console.Out.WriteLine();
        Console.Out.WriteLine("用法:");
        Console.Out.WriteLine("  jcc tool tree [--dir <path>]              列出目录树");
        Console.Out.WriteLine("  jcc tool find <关键词...> [--dir <path>]  按关键词搜索下钻");
        Console.Out.WriteLine("  jcc tool help <节点ID> [--dir <path>]     阅读某节点的完整参数");
        Console.Out.WriteLine("  jcc tool call [<分组>] <工具名> [--参数 值] 进程隔离执行：启动分组 exe 运行工具");
        Console.Out.WriteLine("                                    省略分组时按 node.yaml 子工具清单反查；别名自动换成规范 ID");
        Console.Out.WriteLine("                                    [--exe <path>] 显式指定 exe 路径");
        Console.Out.WriteLine("                                    [--timeout <秒>] 超时保护，默认 180，0 表示不设");
        Console.Out.WriteLine("                                    [--dir <path>] 指定工具目录（不进工具参数）");
        Console.Out.WriteLine("  jcc tool base                             列出 4 个基础工具");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"工具目录优先级: --dir > 环境变量 {ToolsDirEnvVar} > <exe目录>/tools");
        Console.Out.WriteLine($"exe 定位优先级: --exe > {ExeDirEnvVar} > {ToolsDirEnvVar}/<组> > <exe目录>/tools/<组> > 仓库 artifacts/bin");
        Console.Out.WriteLine("每个独立工具也可用 <exe> -h 直接阅读自身说明。");
        return 0;
    }

    private static int WriteUnknownAction(string action) {
        Console.Error.WriteLine($"未知动作 '{action}'（由 jcc tool 的第二个参数触发，CliToolCommand 动作分发层拦截）。");
        Console.Error.WriteLine("可用动作: tree | find | base | help | call");
        Console.Error.WriteLine("正确写法: jcc tool call <分组> <工具名> [--参数 值]");
        return 2;
    }

    /// <summary>
    /// 工具目录解析优先级：--dir > 环境变量 > &lt;jcc目录&gt;/tools > 仓库开发布局。
    /// <para>最后一级是必要的：开发态 exe 在 <c>artifacts/bin/JoinCode/...</c>，旁边没有 tools/ 目录，
    /// 若不回落到仓库里的 <c>modules/tools/cli_nodes/groups</c>，<c>jcc tool tree</c> 会一律显示"未发现 node.yaml"。</para>
    /// </summary>
    private static string ResolveToolsDir(string[] args) {
        var explicitDir = FlatSubCommandRouter.GetOptionValue(args, "--dir");
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
        var envDir = Environment.GetEnvironmentVariable(ToolsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(envDir)) return envDir;

        var published = Path.Combine(AppContext.BaseDirectory, "tools");
        if (Directory.Exists(published)) return published;

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is not null) {
            var devDir = Path.Combine(repoRoot, "modules", "tools", "cli_nodes", "groups");
            if (Directory.Exists(devDir)) return devDir;
        }
        return published;
    }

    private static string ExtractQuery(string[] args) {
        var tokens = new List<string>();
        for (var i = 2; i < args.Length; i++) {
            if (args[i] == "--dir") {
                i++;
                continue;
            }
            if (args[i].StartsWith("--")) continue;
            tokens.Add(args[i]);
        }
        return string.Join(" ", tokens);
    }
}
