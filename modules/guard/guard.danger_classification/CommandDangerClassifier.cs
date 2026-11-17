namespace Core.Security.DangerClassification;

/// <summary>
/// 命令危险分类器实现 — 统一的命令危险分级入口，使用 DangerousCommandCatalog 作为唯一数据源
/// 替代原 DestructiveCommandDetector 的检测逻辑，以 CommandDangerLevel 作为权限决策的唯一依据
/// </summary>
[Register(typeof(ICommandDangerClassifier), ServiceLifetime.Singleton)]
public sealed partial class CommandDangerClassifier : ServiceEntity, ICommandDangerClassifier {
    /// <summary>
    /// 分类信号 — 单个检查层产出的危险等级+风险类型+详情
    /// </summary>
    internal readonly record struct Signal(CommandDangerLevel Level, CommandRisk Risk, string Detail);

    /// <summary>
    /// 信号聚合器 — 累积所有信号的危险等级/风险/详情列表
    /// </summary>
    internal sealed record SignalAccumulator(
        List<CommandDangerLevel> Levels,
        List<CommandRisk> Risks,
        List<string> Details);

    /// <inheritdoc />
    public DangerClassificationResult Classify(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return DangerClassificationResult.SafeResult;

        // 复合命令必须逐段判定再取最严：本分类器按「首词查表」工作，
        // 若不拆段，`echo hi; rm -rf ./build` 会因首词是 echo 被判 Safe —— 而 Safe 是所有权限模式下
        // 直接放行的级别，等于给危险子命令加一个无害前缀就绕过了拦截（Auto 模式下单跑 `rm -rf ./build`
        // 是 Execution，会被拒；加了前缀反而放行，这是明确的等级降级）。
        var segments = ShellCommand.SplitSubcommands(command);
        if (segments.Count <= 1)
            return Classify(ShellCommand.Parse(command));

        var worst = DangerClassificationResult.SafeResult;
        foreach (var segment in segments) {
            worst = PickStricter(worst, Classify(ShellCommand.Parse(segment)));
        }

        return worst;
    }

    /// <summary>
    /// 取两条分类结果里更严的一条；同级别时合并风险类型与详情，便于诊断消息指明是哪一段触发的。
    /// </summary>
    private static DangerClassificationResult PickStricter(
        DangerClassificationResult left,
        DangerClassificationResult right) {
        if ((int)right.Level > (int)left.Level)
            return right;
        if ((int)right.Level < (int)left.Level)
            return left;
        if (left.Level == CommandDangerLevel.Safe)
            return left;

        var risk = left.RiskType != CommandRisk.None ? left.RiskType : right.RiskType;
        var details = string.Join("；",
            new[] { left.Details, right.Details }
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.Ordinal));
        return new DangerClassificationResult(left.Level, risk, details.Length == 0 ? null : details);
    }

    /// <inheritdoc />
    public DangerClassificationResult Classify(ShellCommand command) {
        if (string.IsNullOrWhiteSpace(command.RawCommand))
            return DangerClassificationResult.SafeResult;

        var direct = ClassifyGitEarlyReturn(command)
            ?? CollectSignals(command).Aggregate(new SignalAccumulator([], [], []), AccumulateSignal, BuildResult);

        // 首词定的等级只是「这一条命令」的等级；真正动手的可能在管道右侧或参数里，
        // 那条路径单独判一次再取最严。
        return PickStricter(direct, ClassifyIndirectExecution(command));
    }

    /// <summary>
    /// 间接执行识别 — 真正执行破坏动作的命令可能不在首位。
    /// <para>
    /// 分类器按「首词查表」工作，前缀会盖住后面的真实动作：<c>echo hi | xargs rm -rf</c> 的首词是
    /// <c>echo</c>（Safe），而 Safe 是所有权限模式**直接放行**的级别 —— 加个良性前缀就让拦截失效，
    /// 与 <c>echo hi; rm -rf ./build</c> 是同一类降级。同理 <c>find ... -exec rm -rf {} +</c>
    /// 的命令名是 <c>find</c>，命令藏在参数里。
    /// </para>
    /// <para>
    /// 判定只做**正向**识别（解释器 / 已登记命令 / 危险参数 / 递归强制组合），
    /// 未登记命令**不**产生 Unknown 信号 —— 否则 <c>| jq</c>、<c>| sort</c>、<c>| tee</c>
    /// 这些常见良性管道会在 Auto 模式下被一并拒绝，用户会因为噪音而关掉防护。
    /// </para>
    /// </summary>
    internal static DangerClassificationResult ClassifyIndirectExecution(ShellCommand command)
        => EnumerateIndirectCommands(command)
            .Select(ClassifyIndirectTarget)
            .Aggregate(DangerClassificationResult.SafeResult, PickStricter);

    /// <summary>
    /// 枚举所有「被外层命令带着执行」的内层命令 — 管道右侧 + 参数内嵌（<c>xargs</c> / <c>find -exec</c>）。
    /// <para>
    /// 递归展开是因为间接可以套两层：<c>echo hi | xargs rm -rf</c> 里管道右侧解析出的是 <c>xargs</c>，
    /// 真正执行的是它的参数 <c>rm -rf</c>。层数上限防构造 <c>xargs xargs xargs …</c> 时无限展开。
    /// </para>
    /// </summary>
    internal static IEnumerable<ShellCommand> EnumerateIndirectCommands(ShellCommand command)
        => ExpandIndirect(DirectIndirectTargets(command), depth: 0);

    private const int MaxIndirectDepth = 4;

    private static IEnumerable<ShellCommand> ExpandIndirect(IEnumerable<ShellCommand> targets, int depth) {
        if (depth >= MaxIndirectDepth)
            yield break;

        foreach (var target in targets) {
            yield return target;
            foreach (var nested in ExpandIndirect(DirectIndirectTargets(target), depth + 1)) {
                yield return nested;
            }
        }
    }

    /// <summary>单层的间接目标 — 管道右侧 + 参数内嵌。</summary>
    private static IEnumerable<ShellCommand> DirectIndirectTargets(ShellCommand command)
        => PipelineTargets(command).Concat(EmbeddedCommands(command));

    /// <summary>
    /// 管道右侧的执行单元 — 只取首段之后的，首条命令由调用方按完整口径判定。
    /// </summary>
    private static IEnumerable<ShellCommand> PipelineTargets(ShellCommand command) {
        if (!command.HasPipe) {
            yield break;
        }

        foreach (var stage in ShellCommand.SplitPipelineStages(command.RawCommand).Skip(1)) {
            yield return ResolveExecutedCommand(ShellCommand.Parse(stage));
        }
    }

    /// <summary>
    /// 参数内嵌的执行单元 — <c>xargs &lt;命令&gt;</c> 与 <c>find ... -exec &lt;命令&gt; ... ;</c>。
    /// </summary>
    private static IEnumerable<ShellCommand> EmbeddedCommands(ShellCommand command) {
        if (DangerousCommandCatalog.CommandExecutorCommands.Contains(command.CommandName)) {
            // xargs 的参数才是命令：`xargs -0 -n1 rm -rf` 里 -0/-n1 是 xargs 自己的选项
            if (ResolveCommandPosition(command.Arguments) is { } start) {
                yield return ShellCommand.Parse(string.Join(' ', command.Arguments.Skip(start)));
            }
            yield break;
        }

        foreach (var parts in InlineExecCommandParts(command.Arguments)) {
            yield return ShellCommand.Parse(string.Join(' ', parts));
        }
    }

    /// <summary>
    /// 从参数中取出 <c>-exec</c> / <c>-execdir</c> / <c>-ok</c> / <c>-okdir</c> 后面内嵌的命令词。
    /// <para>终止符是 <c>;</c>（常写作 <c>\;</c>）或 <c>+</c>；<c>{}</c> 是占位符不是命令名，丢掉。</para>
    /// </summary>
    private static IEnumerable<IReadOnlyList<string>> InlineExecCommandParts(IReadOnlyList<string> arguments)
        => Enumerable.Range(0, arguments.Count)
            .Where(i => DangerousCommandCatalog.InlineExecFlags.Contains(arguments[i]))
            .Select(i => arguments.Skip(i + 1)
                .TakeWhile(static token => token is not ";" and not "\\;" and not "+")
                .Where(static token => token != "{}")
                .ToList())
            .Where(static parts => parts.Count > 0);

    /// <summary>
    /// 解析命令位 — 跳过包装器（<c>sudo</c> / <c>env</c> / <c>nohup</c>…）、选项（<c>-E</c>）
    /// 与环境变量赋值（<c>FOO=bar</c>），返回真正被执行的那条命令在参数表中的下标。
    /// <para>
    /// 不跳过包装器就会漏掉 <c>| sudo -E bash</c>（真实命令是 bash 而不是 sudo）；
    /// 但也不能「整段扫一遍找解释器」——那会把 <c>| grep bash file</c> 误判成执行解释器。
    /// </para>
    /// </summary>
    internal static int? ResolveCommandPosition(IReadOnlyList<string> tokens) {
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            if (token.Length == 0 || token.StartsWith('-') || IsEnvAssignment(token))
                continue;
            if (DangerousCommandCatalog.CommandWrapperCommands.Contains(token))
                continue;
            return i;
        }
        return null;
    }

    /// <summary>是否为 <c>NAME=VALUE</c> 形式的环境变量赋值（<c>env</c> 的参数）</summary>
    private static bool IsEnvAssignment(string token) {
        var eq = token.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
            return false;

        for (var i = 0; i < eq; i++) {
            if (!char.IsLetterOrDigit(token[i]) && token[i] != '_')
                return false;
        }
        return true;
    }

    /// <summary>
    /// 剥掉包装器，返回真正会被执行的那条命令（<c>sudo -E bash</c> → <c>bash</c>）。
    /// </summary>
    internal static ShellCommand ResolveExecutedCommand(ShellCommand command) {
        var current = command;
        for (var depth = 0; depth < 3; depth++) {
            if (!DangerousCommandCatalog.CommandWrapperCommands.Contains(current.CommandName))
                return current;
            if (ResolveCommandPosition(current.Arguments) is not { } index)
                return current;
            current = ShellCommand.Parse(string.Join(' ', current.Arguments.Skip(index)));
        }
        return current;
    }

    /// <summary>
    /// 判定一条内层命令 — 解释器直接升到 Execution，其余按「无 Unknown」口径分类。
    /// </summary>
    internal static DangerClassificationResult ClassifyIndirectTarget(ShellCommand target) {
        if (target.CommandName.Length == 0)
            return DangerClassificationResult.SafeResult;

        if (IsInterpreter(target.CommandName)) {
            return new DangerClassificationResult(
                CommandDangerLevel.Execution,
                CommandRisk.RemoteExecution,
                $"'{target.CommandName}' 在管道/参数位置接收内容并解释执行 — 内容可任意，等价于执行任意代码");
        }

        return CollectSignals(target, unknownCommandIsRisk: false)
            .Aggregate(new SignalAccumulator([], [], []), AccumulateSignal, BuildResult);
    }

    /// <summary>
    /// 判定命令是否把内容送进解释器（管道右侧命令位上是解释器）。
    /// </summary>
    internal static bool HasInterpreterPipelineTarget(string rawCommand) {
        if (!rawCommand.Contains('|', StringComparison.Ordinal))
            return false;

        return ShellCommand.SplitPipelineStages(rawCommand)
            .Skip(1)
            .Select(static stage => ResolveExecutedCommand(ShellCommand.Parse(stage)))
            .Any(static stage => IsInterpreter(stage.CommandName));
    }

    /// <summary>
    /// git 子命令前置短路 — 只读子命令检查管道/重定向,不可撤回子命令升级为 Execution
    /// </summary>
    internal static DangerClassificationResult? ClassifyGitEarlyReturn(ShellCommand command) {
        var entry = MatchCommandEntry(command.CommandName);
        if (entry is null || entry.Level != CommandDangerLevel.LightValidation)
            return null;

        if (IsGitReadOnlySubcommand(command))
            return ClassifyGitPipeRedirect(command);

        if (IsGitIrreversibleSubcommand(command))
            return new DangerClassificationResult(
                CommandDangerLevel.Execution,
                CommandRisk.RemoteExecution,
                $"git 不可撤回操作 — {command.Arguments[0]} 涉及远程或删除，无法回滚");

        return null;
    }

    /// <summary>
    /// 收集所有分类信号 — 命令名 → 参数 → 组合 → 递归强制,链式拼接
    /// </summary>
    /// <param name="command">待判定命令</param>
    /// <param name="unknownCommandIsRisk">
    /// 未登记命令是否产出 Unknown（黄灯 ask）。管道右侧 / 参数内嵌的内层命令传 <c>false</c>：
    /// <c>| jq</c>、<c>| sort</c>、<c>| tee</c> 未登记但完全良性，一起判 Unknown 会在 Auto 模式下
    /// 把常见管道全部拒掉。
    /// </param>
    internal static IEnumerable<Signal> CollectSignals(ShellCommand command, bool unknownCommandIsRisk = true)
        => ClassifyByCommandName(command, unknownCommandIsRisk)
            .Concat(ClassifyByArguments(command))
            .Concat(ClassifyByCombinations(command))
            .Concat(ClassifyByRecurseForce(command));

    /// <summary>
    /// 命令名查表 — 已登记命令返回条目信号,未登记返回 Unknown(黄灯)
    /// </summary>
    internal static IEnumerable<Signal> ClassifyByCommandName(ShellCommand command, bool unknownCommandIsRisk = true) {
        var entry = MatchCommandEntry(command.CommandName);
        if (entry is not null) {
            yield return new(entry.Level, entry.RiskType, $"命令 '{command.CommandName}': {entry.Description}");
        } else if (unknownCommandIsRisk) {
            yield return new(CommandDangerLevel.Unknown, CommandRisk.None, $"未知命令 '{command.CommandName}' — 未在 catalog 中登记");
        }
    }

    /// <summary>
    /// 参数检查 — 遍历每个参数,检查危险标志和危险路径
    /// </summary>
    internal static IEnumerable<Signal> ClassifyByArguments(ShellCommand command)
        => command.Arguments.SelectMany(CheckArgumentRisk);

    /// <summary>
    /// 单个参数的风险检查 — 危险标志 + 危险路径
    /// </summary>
    internal static IEnumerable<Signal> CheckArgumentRisk(string arg) {
        if (DangerousCommandCatalog.Flags.TryGetValue(arg, out var flagEntry))
            yield return new(flagEntry.Level, flagEntry.RiskType, $"危险参数 '{arg}': {flagEntry.Description}");

        var pathLevel = ClassifyPath(arg);
        if (pathLevel != CommandDangerLevel.Safe)
            yield return new(pathLevel, CommandRisk.PathEscape, $"危险路径参数 '{arg}': {pathLevel}");
    }

    /// <summary>
    /// 危险组合匹配 — 分位置检查：命令名匹配首个模式，参数匹配剩余模式。
    /// <para>
    /// MTP 扰动纵深防御约束第4条：AC 分位置作用，argv[0] 匹配命令名，argv[1..n] 匹配参数。
    /// 首模式为 "|" 的组合按子串扫描原始串（管道是 shell 语法结构，不是参数）。
    /// </para>
    /// <para>
    /// 注意：当前 catalog **没有**登记任何 "|" 组合 —— 「管道到解释器」已改由
    /// <see cref="ClassifyIndirectExecution"/> 按命令位判定（子串口径会误报 <c>cat notes.sh | head</c>、
    /// 漏报 <c>| xargs rm -rf</c>）。这里保留该分支是为了 catalog 的数据格式完整，不是当前生效的路径。
    /// </para>
    /// </summary>
    internal static IEnumerable<Signal> ClassifyByCombinations(ShellCommand command) {
        var commandNameLower = command.CommandName.ToLowerInvariant();
        var argsLower = command.Arguments.Select(static a => a.ToLowerInvariant()).ToList();
        var rawLower = command.RawCommand.ToLowerInvariant();

        return DangerousCommandCatalog.Combinations
            .Where(c => IsCombinationHit(c, commandNameLower, argsLower, rawLower))
            .Select(c => new Signal(c.Level, c.RiskType, $"危险组合: {c.Description}"));
    }

    /// <summary>
    /// 判断危险组合是否命中 — 分位置匹配。
    /// <para>
    /// 管道组合（首模式为 "|"）扫描原始串；命令组合检查 CommandName + Arguments。
    /// </para>
    /// </summary>
    internal static bool IsCombinationHit(
        DangerousCommandCatalog.CombinationEntry combo,
        string commandNameLower,
        IReadOnlyList<string> argsLower,
        string rawLower) {
        var patterns = combo.LowerPatterns;

        if (patterns[0] == "|")
            return patterns.All(p => rawLower.Contains(p, StringComparison.Ordinal));

        if (!CommandNameMatches(commandNameLower, patterns[0]))
            return false;

        return patterns[1..].All(p => argsLower.Any(a => a.Contains(p, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 命令名匹配 — 精确匹配或前缀匹配（如 mkfs → mkfs.ext4）。
    /// </summary>
    internal static bool CommandNameMatches(string commandNameLower, string pattern)
        => commandNameLower.Equals(pattern, StringComparison.Ordinal)
           || commandNameLower.StartsWith(pattern + ".", StringComparison.Ordinal);

    /// <summary>
    /// 递归+强制组合检查 — Remove-Item/rm/del/erase 的 -Recurse -Force 组合
    /// </summary>
    internal static IEnumerable<Signal> ClassifyByRecurseForce(ShellCommand command) {
        var level = CheckRecurseForceCombination(command);
        if (level == CommandDangerLevel.Safe)
            return [];

        return [
            new(level, CommandRisk.RecursiveOperation, "递归 + 强制组合 — 极度危险"),
            new(level, CommandRisk.ForceOperation, string.Empty),
        ];
    }

    /// <summary>
    /// 累积信号到聚合器
    /// </summary>
    internal static SignalAccumulator AccumulateSignal(SignalAccumulator acc, Signal signal) {
        acc.Levels.Add(signal.Level);
        if (signal.Risk != CommandRisk.None)
            acc.Risks.Add(signal.Risk);
        if (!string.IsNullOrEmpty(signal.Detail))
            acc.Details.Add(signal.Detail);
        return acc;
    }

    /// <summary>
    /// 从聚合器构建最终分类结果 — 取最高等级 + 选最高优先级风险 + 拼接详情
    /// </summary>
    internal static DangerClassificationResult BuildResult(SignalAccumulator acc) {
        var finalLevel = DangerousCommandCatalog.MergeLevels([.. acc.Levels]);
        var primaryRisk = SelectPrimaryRisk(acc.Risks);
        var detailText = acc.Details.Count > 0 ? string.Join("; ", acc.Details) : null;
        return new DangerClassificationResult(finalLevel, primaryRisk, detailText);
    }

    /// <inheritdoc />
    public bool IsDangerous(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var classification = Classify(command);
        return classification.IsDangerous;
    }

    /// <inheritdoc />
    public CommandDangerLevel GetCommandLevel(string commandName) {
        if (string.IsNullOrWhiteSpace(commandName))
            return CommandDangerLevel.Safe;

        var entry = MatchCommandEntry(commandName);
        return entry?.Level ?? CommandDangerLevel.Unknown;
    }

    /// <summary>
    /// 匹配命令条目（支持前缀匹配，如 mkfs.ext4 匹配 mkfs）
    /// </summary>
    internal static DangerousCommandCatalog.CommandEntry? MatchCommandEntry(string commandName) {
        if (DangerousCommandCatalog.Commands.TryGetValue(commandName, out var entry))
            return entry;

        // 前缀匹配：mkfs.ext4 → mkfs
        foreach (var (key, value) in DangerousCommandCatalog.Commands) {
            if (commandName.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase) ||
                commandName.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>
    /// 判断 git 子命令是否为只读（不修改仓库状态） — 委托 GitCommandCatalog.ReadOnlySubcommands 唯一数据源
    /// </summary>
    internal static bool IsGitReadOnlySubcommand(ShellCommand command) {
        if (!command.CommandName.Equals("git", StringComparison.OrdinalIgnoreCase))
            return false;

        // git 无参数 → 只读（显示用法）
        if (command.Arguments.Count == 0)
            return true;

        var subcommand = command.Arguments[0].ToLowerInvariant();

        // branch -D/-d 是删除分支，不是只读
        if (subcommand == "branch") {
            return !command.Arguments.Any(a =>
                a.Equals("-D", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--delete", StringComparison.OrdinalIgnoreCase));
        }

        // stash list 是只读，其他 stash 操作不是
        if (subcommand == "stash") {
            return command.Arguments.Count >= 2 &&
                   command.Arguments[1].Equals("list", StringComparison.OrdinalIgnoreCase);
        }

        // config --get 是只读，config 写入不是
        if (subcommand == "config") {
            return command.Arguments.Any(a =>
                a.Equals("--get", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--get-all", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-l", StringComparison.OrdinalIgnoreCase));
        }

        // fetch --dry-run 是只读
        if (subcommand == "fetch") {
            return command.Arguments.Any(a =>
                a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        }

        return GitCommandCatalog.ReadOnlySubcommands.Contains(subcommand);
    }

    /// <summary>
    /// 判断 git 子命令是否为远程不可撤回操作（升级为 Execution 红灯ask）
    /// git push 推送到远程后无法撤回，git stash drop/tag -d/branch -D 删除操作不可恢复
    /// </summary>
    internal static bool IsGitIrreversibleSubcommand(ShellCommand command) {
        if (!command.CommandName.Equals("git", StringComparison.OrdinalIgnoreCase))
            return false;

        if (command.Arguments.Count == 0)
            return false;

        var subcommand = command.Arguments[0].ToLowerInvariant();

        // git push — 推送到远程，不可撤回（他人已 fetch/pull 后无法回滚）
        if (subcommand == "push")
            return true;

        // git stash drop — 删除 stash，不可撤回
        if (subcommand == "stash" && command.Arguments.Count >= 2 &&
            command.Arguments[1].Equals("drop", StringComparison.OrdinalIgnoreCase))
            return true;

        // git tag -d / tag --delete — 删除标签，不可撤回
        if (subcommand == "tag" && command.Arguments.Any(a =>
            a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--delete", StringComparison.OrdinalIgnoreCase)))
            return true;

        // git branch -D / branch -d / branch --delete — 删除分支，不可撤回
        if (subcommand == "branch" && command.Arguments.Any(a =>
            a.Equals("-D", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--delete", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// 分类路径参数的危险等级
    /// </summary>
    internal static CommandDangerLevel ClassifyPath(string arg) {
        if (string.IsNullOrWhiteSpace(arg))
            return CommandDangerLevel.Safe;

        if (DangerousCommandCatalog.DangerousPaths.TryGetValue(arg, out var level))
            return level;

        // Win32 长路径前缀直接检测 — \\?\ 和 \\.\ 绕过 Win32 路径解析（ADR 0012）
        // 现有前缀匹配逻辑不适合这种前缀（\\?\D:\path 不匹配 \\?\ + \），需单独检测
        if (arg.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
            return CommandDangerLevel.Execution;

        foreach (var (path, pathLevel) in DangerousCommandCatalog.DangerousPaths) {
            if (arg.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))
                return pathLevel;
        }

        return CommandDangerLevel.Safe;
    }

    /// <summary>
    /// 检查 Remove-Item/rm/del/erase 的 -Recurse -Force 组合 — 委托 DangerousCommandCatalog.FileDeletionCommands 唯一数据源
    /// </summary>
    internal static CommandDangerLevel CheckRecurseForceCombination(ShellCommand command) {
        if (!DangerousCommandCatalog.FileDeletionCommands.Contains(command.CommandName))
            return CommandDangerLevel.Safe;

        var hasRecurse = command.Arguments.Any(a =>
            a.Equals("-recurse", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-R", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith('-') && a.Contains('r', StringComparison.OrdinalIgnoreCase));

        var hasForce = command.Arguments.Any(a =>
            a.Equals("-force", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/f", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/q", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith('-') && a.Contains('f', StringComparison.OrdinalIgnoreCase));

        if (hasRecurse && hasForce) {
            // 检查是否针对根目录 — 如果是则 Forbidden，否则 Critical
            var hasRootTarget = command.Arguments.Any(a =>
                a.Equals("/", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("C:\\", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("C:/", StringComparison.OrdinalIgnoreCase));
            return hasRootTarget ? CommandDangerLevel.Dangerous : CommandDangerLevel.Execution;
        }

        return CommandDangerLevel.Safe;
    }

    /// <summary>
    /// 选择最高优先级的风险类型（用于消息构建） — 委托给 <see cref="DangerousCommandCatalog.SelectPrimaryRisk"/> 唯一数据源
    /// </summary>
    internal static CommandRisk SelectPrimaryRisk(IReadOnlyList<CommandRisk> risks)
        => DangerousCommandCatalog.SelectPrimaryRisk(risks);

    /// <summary>
    /// 检查 git 只读命令的管道/重定向 — 管道传入解释器可执行任意代码(Execution),其他管道/重定向需确认(LightValidation)
    /// </summary>
    internal static DangerClassificationResult ClassifyGitPipeRedirect(ShellCommand command) {
        if (!command.HasPipe && !command.HasRedirection)
            return DangerClassificationResult.SafeResult;

        // 用命令位口径而不是「管道后第一个词」：`git log | sudo -E bash` 的目标是 bash 不是 sudo。
        if (command.HasPipe && HasInterpreterPipelineTarget(command.RawCommand)) {
            return new DangerClassificationResult(
                CommandDangerLevel.Execution,
                CommandRisk.RemoteExecution,
                "git 只读命令通过管道传入解释器 — 管道目标可执行任意代码,禁止自动放行");
        }

        return new DangerClassificationResult(
            CommandDangerLevel.LightValidation,
            CommandRisk.None,
            "git 命令含管道/重定向 — 需确认后方可执行");
    }

    /// <summary>
    /// 判断命令名是否为解释器(可执行任意代码) — 引用 DangerousCommandCatalog.InterpreterCommands 唯一数据源
    /// </summary>
    internal static bool IsInterpreter(string commandName)
        => DangerousCommandCatalog.InterpreterCommands.Contains(commandName);
}