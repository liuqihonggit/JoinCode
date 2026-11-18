





namespace JoinCode.Abstractions.Tools;

/// <summary>分组 exe 内的单个子命令实现：入参是已解析的 <c>--key value</c> 字典，返回值是进程退出码。</summary>
/// <param name="options">参数表，键不含前缀 <c>--</c>，大小写不敏感。</param>
/// <param name="cancellationToken">Ctrl+C 取消令牌。</param>
public delegate Task<int> CliNodeHandler(IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken);

/// <summary>
/// 分组 exe 的声明式定义 —— 各组 Program.cs 只需声明「我是谁 + 我有哪些本地实现」。
/// <para>存在理由：13 个分组 exe 的入口逻辑（-h / --list-local / 参数解析 / 本地分发 / 转发宿主 / 退出码）
/// 若各写一份，13 份就会慢慢长出 13 种细节差异（这正是"逐组做实"最容易失控的地方）。
/// 收成一处后，新增分组只写一个字典，行为天然一致。</para>
/// </summary>
public sealed class CliNodeDefinition {
    /// <summary>分组 ID，与 group_map.yaml / node.yaml 的 id 一致。</summary>
    public required string GroupId { get; init; }

    /// <summary>内嵌帮助资源的逻辑名。</summary>
    public required string HelpResource { get; init; }

    /// <summary>帮助输出里的分组名，如 <c>web 分组</c>。</summary>
    public required string Title { get; init; }

    /// <summary>按工具 ID 取本地实现；返回 null 表示该工具需转发宿主。</summary>
    public required Func<string, CliNodeHandler?> Resolve { get; init; }

    /// <summary>本 exe 真实注册的本地工具 ID，供 <c>--list-local</c> 自报。</summary>
    public required Func<IEnumerable<string>> LocalToolIds { get; init; }

    /// <summary>转发宿主时读取宿主 exe 路径的环境变量名。</summary>
    public string HostEnvironmentVariable { get; init; } = "JCC_HOST_EXE";

    /// <summary>转发宿主时的默认 exe 名。</summary>
    public string HostExecutable { get; init; } = "jcc";

    /// <summary>
    /// 转发宿主时使用的子命令 — 工具组走 <c>mcp_call</c>，斜杠命令组走 <c>slash_call</c>。
    /// </summary>
    public string HostVerb { get; init; } = "mcp_call";

    /// <summary>
    /// 工具名归一化：斜杠命令要处理 Git Bash 把 <c>/agents</c> 展开成 Windows 路径的情况，
    /// 工具组则保持原名。默认不做变换。
    /// </summary>
    public Func<string, string> NameNormalizer { get; init; } = static name => name;

    /// <summary>
    /// 转发宿主时工具名的归一化 — 与 <see cref="NameNormalizer"/> 分开：
    /// 前者改的是传给宿主的命令名（如斜杠命令 /agents → agents），后者改的是调用方给的输入名。
    /// 默认不变；斜杠组设为去掉前导 '/'。
    /// </summary>
    public Func<string, string> HostNameNormalizer { get; init; } = static name => name;

    /// <summary>
    /// 工具由另一个独立 exe 执行时的目标名（如 <c>jcc-gh</c>）；为空表示转发宿主。
    /// <para>存在理由：github 组 172 个工具的实现已经在 <c>modules/tools/gh</c> 里，
    /// 且那本身就是一个独立 exe。把这 18K 行搬进分组 exe 只会得到两份会漂移的实现；
    /// 委托过去同样是跨进程隔离，且不经过宿主的引擎与 Hub。</para>
    /// </summary>
    public string? DelegateExecutable { get; init; }

    /// <summary>委托 exe 路径的环境变量名（可选）；未设置时按「本 exe 同目录 → PATH」查找。</summary>
    public string? DelegateEnvironmentVariable { get; init; }

    /// <summary>委托调用时插在工具名之前的动词，如 <c>--call-tool</c>。</summary>
    public string DelegateVerb { get; init; } = "--call-tool";

    /// <summary>委托 exe 缺失时的原因说明。</summary>
    public string DelegateReason { get; init; } = "该组的工具由另一个独立 exe 实现，当前部署缺少它。";

    /// <summary>本地实现依赖宿主独有能力时的说明，出现在转发失败提示里。</summary>
    public string HostReason { get; init; } = "该工具依赖宿主独有能力（引擎服务 / 共享状态 / 模型调用），零依赖 exe 无法等价实现。";
}

/// <summary>
/// 分组工具 exe 的统一入口 —— 全部 13 个分组共用，避免入口行为逐组漂移。
/// <para>退出码约定（与 jcc tool call 执行器一致）：0 成功 / 2 参数或本地执行错误 / 3 宿主缺失或启动失败。</para>
/// </summary>
public static class CliNodeRunner {
    /// <summary>成功。</summary>
    public const int ExitSuccess = 0;

    /// <summary>参数错误或本地执行失败。</summary>
    public const int ExitInvalidArguments = 2;

    /// <summary>需要宿主但宿主不可用。</summary>
    public const int ExitHostUnavailable = 3;

    private const string HelpMarker = "  {0} — ";
    private const string ToolListHeader = "工具列表";

    /// <summary>运行分组 exe。</summary>
    /// <param name="definition">分组声明。</param>
    /// <param name="args">命令行参数（不含 exe 自身）。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> RunAsync(CliNodeDefinition definition, string[] args) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(args);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };
        return await RunCoreAsync(definition, args, cts.Token).ConfigureAwait(false);
    }

    private static async Task<int> RunCoreAsync(CliNodeDefinition definition, string[] args, CancellationToken ct) {
        var help = LoadHelp(definition.HelpResource);

        if (args.Length == 0) {
            Write(RenderIndex(help));
            return ExitSuccess;
        }
        if (IsHelp(args[0])) {
            // `-h <关键字>` 检索详情：大组（github 172 个）没有这个入口就只能整段倒出来。
            Write(args.Length > 1 ? RenderMatch(help, args[1]) : RenderIndex(help));
            return ExitSuccess;
        }
        if (CliNodeCoverage.IsRequested(args[0])) {
            Write(CliNodeCoverage.Render(
                definition.GroupId,
                definition.LocalToolIds(),
                definition.DelegateExecutable,
                definition.DelegateExecutable is null ? null : ResolveDelegatePath(definition)));
            return ExitSuccess;
        }

        // 名字归一化：斜杠命令要把 Git Bash 展开的 Windows 路径还原成 /agents。
        var requested = definition.NameNormalizer(args[0]);
        var known = ListToolIds(help);
        var canonical = CanonicalToolId(definition.GroupId, requested, known);
        var unknown = known.Count > 0 && canonical.Length == 0;

        if (args.Length > 1 && IsHelp(args[1])) {
            if (unknown) {
                WriteError(BuildUnknownToolMessage(definition, requested, known));
                return ExitInvalidArguments;
            }
            PrintToolHelp(definition, help, canonical.Length > 0 ? canonical : requested);
            return ExitSuccess;
        }

        if (unknown) {
            WriteError(BuildUnknownToolMessage(definition, requested, known));
            return ExitInvalidArguments;
        }
        // 工具名一律换成帮助里的规范写法：exe 的本地实现表与详情段都用规范 ID 索引。
        var toolId = canonical.Length > 0 ? canonical : requested;

        var options = ParseOptions(args.AsSpan(1));
        var handler = definition.Resolve(toolId);
        if (handler is not null) return await RunLocalAsync(definition, toolId, handler, NormalizeOptionNames(options, help, toolId), ct).ConfigureAwait(false);

        return await ForwardAsync(definition, toolId, NormalizeOptionNames(options, help, toolId), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 把命令行里的参数名归一化到该工具声明的规范名。
    /// <para>例：工具声明 <c>file_path</c>，调用方写 <c>--filePath</c>，归一化后命中同一个参数。
    /// 这条链路必须与宿主侧（中间件 <c>ArgumentRepairMiddleware</c>）给出同一个答案 ——
    /// 否则同一次调用在 host 路径被自动修复、在 local 路径被拒，调用方只能逐个试错。</para>
    /// <para>规则来自契约层的 <see cref="ToolParameterNameRepairer"/>（唯一真相）；
    /// 帮助文本里解析不到参数名时保持原样，由工具自己报「缺少必填参数」并给出正确写法。</para>
    /// </summary>
    internal static Dictionary<string, string> NormalizeOptionNames(
        Dictionary<string, string> options, string help, string toolId) {
        if (options.Count == 0) return options;
        var declared = DeclaredParameterNames(help, toolId);
        return declared.Count == 0 ? options : ToolParameterNameRepairer.RepairNames(options, declared);
    }

    /// <summary>
    /// 从帮助文本里解析该工具声明的参数名集合 — 详情段里形如 <c>    --参数名  类型  必填  说明</c> 的行。
    /// <para>解析不到就返回空集合（不伪造参数名）：宁可让工具报缺参，也不要把参数映射到错的参数上。</para>
    /// </summary>
    internal static List<string> DeclaredParameterNames(string help, string toolId) {
        var names = new List<string>();
        var section = DetailSectionOf(help.Replace("\r\n", "\n"), toolId);
        if (section.Length == 0) return names;
        foreach (var line in section.Split('\n')) {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("--", StringComparison.Ordinal)) continue;
            var name = trimmed[2..];
            var space = name.IndexOfAny([' ', '\t', '=']);
            if (space > 0) name = name[..space];
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// 把调用方给的名字换成帮助清单里的规范 ID；找不到返回空串。
    /// <para>需要这一层的直接原因：同一组里 <c>read</c> 与 <c>file_read</c> 是同一个工具、
    /// <c>rm</c> 就是删文件，调用方不该先猜「这次要不要带 <c>file_</c>」。</para>
    /// <para>判定规则来自契约层 <see cref="ToolAliasTable"/>（唯一数据源）——
    /// 与宿主 <c>jcc tool call</c> 用的是同一份，否则同一次调用在两条路径上一个通一个「工具不存在」。</para>
    /// </summary>
    private static string CanonicalToolId(string groupId, string requested, List<string> known) {
        if (known.Count == 0) return requested;
        return ToolAliasTable.Resolve(groupId, known, requested) ?? string.Empty;
    }

    private static async Task<int> RunLocalAsync(
        CliNodeDefinition definition, string toolId, CliNodeHandler handler,
        IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        try {
            return await handler(options, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            WriteError($"工具 '{toolId}' 已取消。");
            return ExitInvalidArguments;
        } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                         or HttpRequestException or TimeoutException or NotSupportedException
                                         or InvalidOperationException) {
            var reason = ex.Message;
            WriteError($"工具 '{toolId}' 本地执行失败（{definition.GroupId} 本地执行层拦截）：{reason}");
            WriteError($"修复方式：{HintFor(ex, toolId)}");
            return ExitInvalidArguments;
        }
    }

    private static string HintFor(Exception ex, string toolId) => ex switch {
        ArgumentException => $"{toolId} -h 查看必填参数与取值范围。",
        HttpRequestException => "检查 URL 与网络连通性；受限站点改用宿主 web_fetch。",
        UnauthorizedAccessException => "目标路径无访问权限，换一个可写目录再试。",
        TimeoutException => "操作超时，缩小搜索范围（--path）或提高宿主超时后重试。",
        _ => "检查路径、磁盘空间与参数取值；必要时改用宿主执行。"
    };

    private static async Task<int> ForwardAsync(
        CliNodeDefinition definition, string toolId, Dictionary<string, string> options, CancellationToken ct) {
        // 委托别的独立 exe 时，宿主完全不参与 —— 这是「进程隔离」，不是「转发宿主」。
        if (!string.IsNullOrWhiteSpace(definition.DelegateExecutable)) {
            return await DelegateAsync(definition, toolId, options, ct).ConfigureAwait(false);
        }

        var host = Environment.GetEnvironmentVariable(definition.HostEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(host)) host = definition.HostExecutable;

        var payload = JsonSerializer.Serialize(options, CliNodeJsonContext.Default.DictionaryStringString);
        var psi = new ProcessStartInfo(host) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(definition.HostVerb);
        psi.ArgumentList.Add(definition.HostNameNormalizer(toolId));
        psi.ArgumentList.Add(payload);

        try {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动宿主进程");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Write(await stdout.ConfigureAwait(false));
            WriteError(await stderr.ConfigureAwait(false), newline: false);
            return process.ExitCode;
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
            WriteError($"工具 '{toolId}' 无本地实现且需宿主 jcc 才能执行（转发层拦截）：{ex.Message}");
            WriteError($"原因：{definition.HostReason}");
            WriteError($"修复方式：设置 {definition.HostEnvironmentVariable} 指向 jcc.exe，或把 jcc 加入 PATH；也可用 -h 只查看说明。");
            return ExitHostUnavailable;
        }
    }

    /// <summary>
    /// 把调用交给另一个独立 exe。<c>--k v</c> 原样透传，退出码原样返回。
    /// <para>与宿主转发分开的理由：委托目标自带实现与凭据，不需要引擎 DI / Hub / 会话状态，
    /// 因此它失败时的修复动作也不同（缺的是那个 exe，不是宿主）。</para>
    /// </summary>
    private static async Task<int> DelegateAsync(
        CliNodeDefinition definition, string toolId, Dictionary<string, string> options, CancellationToken ct) {
        var exe = ResolveDelegatePath(definition);
        if (exe is null) {
            WriteError($"工具 '{toolId}' 需要独立执行器 '{definition.DelegateExecutable}'，但没有找到它（委托层拦截）。");
            WriteError($"原因：{definition.DelegateReason}");
            WriteError($"修复方式：{DelegateLocateHint(definition)}");
            return ExitHostUnavailable;
        }

        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(definition.DelegateVerb);
        psi.ArgumentList.Add(toolId);
        foreach (var pair in options) {
            // false / 空值不传：帮助里「默认=true」的开关，LLM 显式写 false 才需要落到命令行。
            psi.ArgumentList.Add("--" + pair.Key);
            psi.ArgumentList.Add(pair.Value);
        }

        try {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动委托进程");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Write(await stdout.ConfigureAwait(false));
            WriteError(await stderr.ConfigureAwait(false), newline: false);
            return process.ExitCode;
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
            WriteError($"工具 '{toolId}' 的独立执行器 '{exe}' 启动失败（委托层拦截）：{ex.Message}");
            WriteError($"修复方式：{DelegateLocateHint(definition)}");
            return ExitHostUnavailable;
        }
    }

    /// <summary>
    /// 定位委托 exe：环境变量 → 本 exe 同目录 → 同目录 <c>delegate/</c> → 上一级目录 → PATH。
    /// <para>带上一级目录是因为委托实现可能很重（jcc-gh 连依赖共 20 MB），
    /// 按组各放一份不划算，装配脚本只在 tools/ 根放一份。</para>
    /// <para>带 <c>delegate/</c> 子目录是因为开发布局下 gh 产物必须拷到组输出目录里才不依赖 PATH，
    /// 但它的依赖（整套引擎 80+ DLL）不能平铺 —— 平铺会撞掉本组自己的同名 DLL，
    /// 症状是「重新编译了但行为没变」（用旧契约层盖掉新编的）。</para>
    /// </summary>
    private static string? ResolveDelegatePath(CliNodeDefinition definition) {
        var name = definition.DelegateExecutable;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!string.IsNullOrWhiteSpace(definition.DelegateEnvironmentVariable)) {
            var configured = Environment.GetEnvironmentVariable(definition.DelegateEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        }
        var baseDir = AppContext.BaseDirectory;
        var searchDirs = new[] { baseDir, Path.Combine(baseDir, "delegate"), Path.Combine(baseDir, "..") };
        foreach (var dir in searchDirs) {
            foreach (var candidate in CandidateNames(name)) {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full)) return Path.GetFullPath(full);
            }
        }
        // 都没有就交给系统 PATH：装了 jcc-gh 的机器不必再拷一份到分组目录。
        return OnPath(name);
    }

    private static string[] CandidateNames(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? [name] : [name, name + ".exe"];

    private static string? OnPath(string name) {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            foreach (var candidate in CandidateNames(name)) {
                try {
                    var full = Path.Combine(dir.Trim(), candidate);
                    if (File.Exists(full)) return full;
                } catch (ArgumentException) {
                    // PATH 里混进非法字符的目录时跳过，不要因此整条查找失败。
                }
            }
        }
        return null;
    }

    private static string DelegateLocateHint(CliNodeDefinition definition) {
        var env = definition.DelegateEnvironmentVariable;
        var target = definition.DelegateExecutable;
        return string.IsNullOrWhiteSpace(env)
            ? $"把 {target}.exe 放到本程序同目录（或其中的 delegate/ 子目录）、上一级目录、或加入 PATH。"
            : $"把 {target}.exe 放到本程序同目录（或其中的 delegate/ 子目录）/ 上一级目录 / 加入 PATH，或设置 {env} 指向它。";
    }

    private static string BuildUnknownToolMessage(CliNodeDefinition definition, string toolId, List<string> known) {
        var builder = new StringBuilder(256);
        builder.Append("未知工具 '").Append(toolId).Append("'（").Append(definition.Title).AppendLine("内不存在）。");
        var suggestions = Suggest(toolId, known);
        if (suggestions.Count > 0) {
            builder.Append("你是不是想用：").AppendLine(string.Join(" / ", suggestions));
        }
        // 提示里写真实 exe 名：写分组 ID（github）会让人去敲 `github -h`，那不是可执行文件。
        builder.Append("执行 ").Append(ExecutableNameOf(definition)).AppendLine(" -h 查看本组全部工具。");
        return builder.ToString();
    }

    /// <summary>当前进程的可执行文件名；拿不到时退回分组 ID，保证提示总有内容。</summary>
    private static string ExecutableNameOf(CliNodeDefinition definition) {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return definition.GroupId;
        var name = Path.GetFileNameWithoutExtension(processPath);
        return string.IsNullOrWhiteSpace(name) ? definition.GroupId : name;
    }

    /// <summary>近似名建议：包含关系优先，其次编辑距离不超过 2。</summary>
    internal static List<string> Suggest(string input, IReadOnlyList<string> candidates) {
        var lowered = input.ToLowerInvariant();
        var contains = candidates
            .Where(id => id.Contains(lowered, StringComparison.Ordinal) || lowered.Contains(id, StringComparison.Ordinal))
            .Take(5)
            .ToList();
        if (contains.Count > 0) return contains;

        return candidates
            .Select(id => (Id: id, Distance: EditDistance(lowered, id)))
            .Where(pair => pair.Distance <= 2)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Id)
            .Take(5)
            .ToList();
    }

    private static int EditDistance(string left, string right) {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++) {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++) {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            Array.Copy(current, previous, current.Length);
        }
        return previous[right.Length];
    }

    private static void PrintToolHelp(CliNodeDefinition definition, string help, string toolId) {
        var normalized = help.Replace("\r\n", "\n");
        var marker = string.Format(System.Globalization.CultureInfo.InvariantCulture, HelpMarker, toolId);
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) {
            Write(normalized);
            return;
        }
        var boundaries = new[] {
            normalized.IndexOf("\n\n  ", start + marker.Length, StringComparison.Ordinal),
            normalized.IndexOf("\n描述来源:", start, StringComparison.Ordinal),
            normalized.Length
        }.Where(index => index > start).ToArray();
        var end = boundaries.Length > 0 ? boundaries.Min() : normalized.Length;
        var mode = definition.Resolve(toolId) is not null
            ? "local（本 exe 内零依赖实现）"
            : definition.DelegateExecutable is not null
                ? DelegateModeLabel(definition)
                : $"host（转发宿主 jcc {definition.HostVerb}）";
        Write($"{definition.Title} / {toolId} — 参数说明（复用现有 [McpToolParameter] 特性）\n");
        Write($"执行方式: {mode}\n");
        // 等价写法：同一组里 read 与 file_read 是同一个工具，rm 就是删文件。
        // 不写出来，调用方只能靠猜「这次要不要带 file_」，猜错就以为工具不存在。
        var aliases = ToolAliasTable.AliasesOf(definition.GroupId, toolId).Skip(1).ToArray();
        if (aliases.Length > 0)
            Write($"等价写法: {string.Join(" / ", aliases)}\n");
        Write(normalized[start..end].TrimEnd() + "\n");
    }

    /// <summary>
    /// delegate 的执行方式说明 —— 必须带上解析到的绝对路径。
    /// <para>委托 exe 可能来自同目录、上级目录或 PATH，只有前两者是本次构建的产物；
    /// 不打印路径就无从判断「跑的是刚编的还是几个月前部署的那份」，这正是 ADR 0132 记过的坑。</para>
    /// </summary>
    private static string DelegateModeLabel(CliNodeDefinition definition) {
        var resolved = ResolveDelegatePath(definition);
        return resolved is null
            ? $"delegate（独立执行器 {definition.DelegateExecutable}，不经宿主；当前未找到，调用会退出 3）"
            : $"delegate（独立执行器 {definition.DelegateExecutable}，不经宿主）：{resolved}";
    }

    private static string LoadHelp(string resourceName) {
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null) {
            using var stream = entry.GetManifestResourceStream(resourceName);
            if (stream is not null) {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Replace("\r\n", "\n");
            }
        }
        throw new InvalidOperationException($"未找到内嵌帮助资源 '{resourceName}'，请检查 csproj 的 EmbeddedResource 配置。");
    }

    /// <summary>帮助条目：工具 ID + 一行摘要 + 所属类别（斜杠命令按 <c>[Agent]</c> 分节时才有）。</summary>
    private readonly record struct HelpEntry(string Id, string Summary, string Category);

    /// <summary>工具数超过此值时 <c>-h</c> 只输出索引 —— 172 个工具的完整参数表是 70 KB，一次倒出来直接占满上下文。</summary>
    private const int FullHelpEntryLimit = 20;

    /// <summary>
    /// 渲染 <c>-h</c> 输出：小组给全文，大组给压缩索引。
    /// <para>大组索引按名字前缀折叠（github 的 172 个工具折成 30 余行），
    /// 详情仍由 <c>&lt;exe&gt; -h &lt;工具名&gt;</c> 或 <c>-h &lt;关键字&gt;</c> 提供。</para>
    /// </summary>
    internal static string RenderIndex(string help) {
        var normalized = help.Replace("\r\n", "\n");
        var entries = ParseEntries(normalized);
        if (entries.Count <= FullHelpEntryLimit) return normalized;

        var header = HeaderOf(normalized);
        var exe = ExecutableNameOf(header);
        var builder = new StringBuilder(4096);
        builder.Append(header).Append("\n\n");
        builder.Append("工具索引（").Append(entries.Count)
            .Append(" 个，详情: ").Append(exe).Append(" -h <工具名> 或 ").Append(exe).Append(" -h <关键字>）:\n");
        AppendGrouped(builder, entries);
        builder.Append("\n提示: 直接 `").Append(exe).Append(" <工具名> -h` 看单个工具的完整参数。\n");
        return builder.ToString();
    }

    /// <summary>
    /// 从帮助头部取可执行文件名 —— 提示里写死别名或占位符都会让调用方去猜。
    /// <para>优先取用法行里的 <c>xxx.exe</c>：标题行只有 <c>jcc-tool-github</c>，
    /// 在 bash 里不带扩展名敲不出来。</para>
    /// </summary>
    private static string ExecutableNameOf(string header) {
        if (string.IsNullOrWhiteSpace(header)) return "<exe>";
        foreach (var line in header.Split('\n')) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var space = trimmed.IndexOf(' ');
            var candidate = space > 0 ? trimmed[..space] : trimmed;
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        var firstLine = header.Split('\n')[0].Trim();
        var marker = firstLine.IndexOf(" — ", StringComparison.Ordinal);
        var name = marker > 0 ? firstLine[..marker].Trim() : firstLine;
        return name.Length == 0 ? "<exe>" : name;
    }

    private static void AppendGrouped(StringBuilder builder, List<HelpEntry> entries) {
        var groups = BuildGroups(entries, out var folded);
        if (!folded) {
            AppendNameColumns(builder, entries);
            return;
        }
        foreach (var group in groups) {
            // 单级命令（gh_api / gh_status）分组名就是工具名，重复写两遍只会浪费一行宽度。
            if (group.Names.Count == 1 && string.Equals(group.Names[0], group.Prefix, StringComparison.Ordinal)) {
                builder.Append("  ").Append(group.Prefix).Append("  (1)\n");
                continue;
            }
            // 计数写在前缀后而不是行尾：工具多的组要折行，行尾计数会跟着飘到任意一行。
            var indent = $"  {group.Prefix}/ ({group.Names.Count}) ";
            AppendWrapped(builder, indent, "    ", group.Names);
        }
    }

    /// <summary>把名字列表按行宽折行输出，续行用 <paramref name="continuation"/> 缩进。</summary>
    private static void AppendWrapped(StringBuilder builder, string indent, string continuation, List<string> names) {
        const int lineWidth = 96;
        var line = new StringBuilder(indent);
        foreach (var name in names) {
            if (line.Length > indent.Length + 1 && line.Length + name.Length + 2 > lineWidth) {
                builder.Append(line).Append('\n');
                line.Clear().Append(continuation);
            }
            if (line.Length > continuation.Length) line.Append("  ");
            line.Append(name);
        }
        builder.Append(line).Append('\n');
    }

    /// <summary>名字折叠不出效果（斜杠命令没有下划线）时退回等宽名字清单，避免每组一行的空转。</summary>
    private static void AppendNameColumns(StringBuilder builder, List<HelpEntry> entries) {
        const int lineWidth = 96;
        var line = new StringBuilder();
        foreach (var entry in entries) {
            if (line.Length > 0 && line.Length + entry.Id.Length + 2 > lineWidth) {
                builder.Append("  ").Append(line).Append('\n');
                line.Clear();
            }
            if (line.Length > 0) line.Append("  ");
            line.Append(entry.Id);
        }
        if (line.Length > 0) builder.Append("  ").Append(line).Append('\n');
    }

    private readonly record struct HelpGroup(string Prefix, List<string> Names);

    /// <summary>
    /// 分组：帮助里有类别分节（斜杠命令的 <c>[Agent]</c>）优先用类别，
    /// 否则按工具名分段折叠——默认取第一段，若第一段覆盖六成以上工具（github 全是 <c>gh_</c>）改用两段。
    /// </summary>
    private static List<HelpGroup> BuildGroups(List<HelpEntry> entries, out bool folded) {
        var categorized = entries.Count(entry => entry.Category.Length > 0);
        if (categorized * 2 >= entries.Count) {
            var byCategory = entries
                .GroupBy(entry => entry.Category, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new HelpGroup(group.Key, group.Select(entry => entry.Id).ToList()))
                .ToList();
            folded = byCategory.Count * 10 <= entries.Count * 7;
            if (folded) return byCategory;
        }

        var firstSegments = entries
            .Select(entry => entry.Id.Split('_')[0])
            .GroupBy(segment => segment, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToList();
        var widest = firstSegments.Count > 0 ? firstSegments[0].Count() : 0;
        var take = widest * 10 >= entries.Count * 6 ? 2 : 1;

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in entries) {
            var parts = entry.Id.Split('_');
            var depth = Math.Min(take, parts.Length);
            var prefix = string.Join('_', parts[..depth]);
            var name = parts.Length > depth ? string.Join('_', parts[depth..]) : entry.Id;
            if (!map.TryGetValue(prefix, out var names)) {
                names = [];
                map[prefix] = names;
            }
            names.Add(name);
        }
        // 折叠率不足七成说明命名不是层级式（斜杠命令 /advisor 没有下划线），退回名字清单。
        folded = map.Count * 10 <= entries.Count * 7;
        return map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new HelpGroup(pair.Key, pair.Value))
            .ToList();
    }

    /// <summary>按关键字检索工具详情：命中工具名或摘要的工具整段输出。</summary>
    internal static string RenderMatch(string help, string keyword) {
        var normalized = help.Replace("\r\n", "\n");
        var entries = ParseEntries(normalized);
        // 连字符写法（pull-request）与下划线工具名（gh_pr_*）之间要能互认。
        var token = keyword.Replace('-', '_');
        var hits = entries
            .Where(entry => entry.Id.Contains(token, StringComparison.OrdinalIgnoreCase)
                            || entry.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            // 工具名命中排在描述命中之前 —— 前者是直接要查的，后者只是顺带提到。
            .OrderByDescending(entry => entry.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToList();
        if (hits.Count == 0) {
            var suggestions = Suggest(keyword, entries.Select(entry => entry.Id).ToList());
            var exe = ExecutableNameOf(HeaderOf(normalized));
            var message = new StringBuilder($"没有匹配 '{keyword}' 的工具。");
            if (suggestions.Count > 0) message.Append("\n你是不是想用：").Append(string.Join(" / ", suggestions));
            message.Append("\n执行 ").Append(exe).Append(" -h 查看完整工具索引。");
            return message.ToString();
        }

        var builder = new StringBuilder(4096);
        builder.Append("命中 ").Append(hits.Count).Append(" 个工具（").Append(keyword).Append("）:\n");
        foreach (var hit in hits) {
            builder.Append('\n');
            // 详情段本身已含「  id — 摘要」首行；没有详情段时才补一行摘要，避免同一条打印两遍。
            var section = DetailSectionOf(normalized, hit.Id);
            if (section.Length > 0) builder.Append(section);
            else builder.Append("  ").Append(hit.Id).Append(" — ").Append(hit.Summary).Append('\n');
        }
        // 末尾保留换行：Write 不补 \n，缺了会和下一条命令的输出粘在一行。
        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>取某个工具的参数说明段；帮助文本里没有该段时返回空串（不伪造参数）。</summary>
    private static string DetailSectionOf(string normalized, string toolId) {
        var marker = $"  {toolId} — ";
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var next = normalized.IndexOf("\n  ", start + marker.Length, StringComparison.Ordinal);
        while (next > 0 && !IsToolHeaderAt(normalized, next + 1)) {
            next = normalized.IndexOf("\n  ", next + 1, StringComparison.Ordinal);
        }
        var tail = normalized.IndexOf("描述来源:", start, StringComparison.Ordinal);
        var end = next > 0 ? next : normalized.Length;
        if (tail > start && tail < end) end = tail;
        return normalized[start..end].TrimEnd() + "\n";
    }

    /// <summary>判断某行是否是工具段头（<c>  id — 描述</c>），用于划分详情段边界。</summary>
    private static bool IsToolHeaderAt(string text, int lineStart) {
        var lineEnd = text.IndexOf('\n', lineStart);
        var line = lineEnd > 0 ? text[lineStart..lineEnd] : text[lineStart..];
        if (!line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal)) return false;
        return line.Contains(" — ", StringComparison.Ordinal);
    }

    /// <summary>帮助头部（标题 + 用法），即列表段之前的内容。</summary>
    private static string HeaderOf(string normalized) {
        var index = FindListHeader(normalized);
        var header = index > 0 ? normalized[..index] : normalized;
        return header.TrimEnd();
    }

    /// <summary>列表段标题位置 — 工具组是「工具列表」，斜杠命令组是「命令列表」。</summary>
    private static int FindListHeader(string normalized) {
        var best = -1;
        foreach (var marker in ListHeaderMarkers) {
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }
        return best;
    }

    private static readonly string[] ListHeaderMarkers = ["工具列表", "命令列表"];

    /// <summary>从帮助文本的列表段解析工具 ID、摘要与类别。</summary>
    private static List<HelpEntry> ParseEntries(string normalized) {
        var entries = new List<HelpEntry>();
        var headerIndex = FindListHeader(normalized);
        if (headerIndex < 0) return entries;
        var lines = normalized[headerIndex..].Split('\n');
        var category = string.Empty;
        for (var i = 1; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length == 0) continue;
            // 列表段与详情段的分界：「单个工具/命令的参数说明:」
            if (line.StartsWith("单个", StringComparison.Ordinal)) break;
            if (!line.StartsWith("  ", StringComparison.Ordinal)) {
                // 斜杠命令组按 [Agent] (9 个) 分节 —— 这层分类正好能当索引的分组键。
                var heading = line.Trim();
                if (heading.StartsWith('[') && heading.Contains(']')) {
                    var close = heading.IndexOf(']');
                    category = heading[1..close];
                }
                continue;
            }
            if (line.StartsWith("    ", StringComparison.Ordinal)) continue;
            var trimmed = line.Trim();
            var space = trimmed.IndexOf(' ');
            if (space <= 0) continue;
            entries.Add(new HelpEntry(trimmed[..space], trimmed[space..].Trim(), category));
            if (entries.Count >= 1000) break;
        }
        return entries;
    }

    /// <summary>从帮助文本的「工具列表」段解析工具 ID，用于未知工具判定与近似名建议。</summary>
    internal static List<string> ListToolIds(string help) {
        var normalized = help.Replace("\r\n", "\n");
        return ParseEntries(normalized).Select(entry => entry.Id).ToList();
    }

    private static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++) {
            var token = args[i];
            if (IsOptionToken(token)) {
                // 单横线短选项（-i / -A）保留整串原样作键，与帮助里的「别名=-i」一一对应。
                var isLong = token.StartsWith("--", StringComparison.Ordinal);
                var key = isLong ? token[2..] : token;
                var equals = key.IndexOf('=');
                if (equals > 0) {
                    result[key[..equals]] = key[(equals + 1)..];
                    continue;
                }
                if (i + 1 < args.Length && !IsOptionToken(args[i + 1])) {
                    result[key] = args[++i];
                    continue;
                }
                result[key] = "true";
                continue;
            }
            result["arg" + (i + 1)] = token;
        }
        return result;
    }

    /// <summary>
    /// 选项判定：<c>--long</c> / <c>--key=value</c>，或 <c>-x</c>（排除 <c>-5</c> 这类负数，避免把值当选项）。
    /// <para>长选项必须真的是「键」才算：unified diff 的内容以 <c>---</c> 开头，多行 patch 更含空格与换行，
    /// 早先只判 <c>StartsWith("--")</c> 时会把整段 patch 当成选项名，导致 file_apply_patch 永远报「没有可用的 hunk」。</para>
    /// </summary>
    private static bool IsOptionToken(string token) {
        if (token.Length > 2 && token[0] == '-' && token[1] == '-') {
            for (var i = 2; i < token.Length; i++) {
                var c = token[i];
                if (c == '=') return true;
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            }
            return true;
        }
        return token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]);
    }

    private static bool IsHelp(string token) => token is "-h" or "--help" or "help";

    private static void Write(string text) => Console.Out.Write(text);

    private static void WriteError(string text, bool newline = true) {
        if (newline) Console.Error.WriteLine(text);
        else Console.Error.Write(text);
    }
}

/// <summary>转发宿主时的参数序列化上下文 —— 源生成避免 AOT 下的反射序列化。</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CliNodeJsonContext : JsonSerializerContext { }
