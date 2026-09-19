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
    private readonly record struct Signal(CommandDangerLevel Level, CommandRisk Risk, string Detail);

    /// <summary>
    /// 信号聚合器 — 累积所有信号的危险等级/风险/详情列表
    /// </summary>
    private sealed record SignalAccumulator(
        List<CommandDangerLevel> Levels,
        List<CommandRisk> Risks,
        List<string> Details);

    /// <inheritdoc />
    public DangerClassificationResult Classify(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return DangerClassificationResult.SafeResult;

        return Classify(ShellCommand.Parse(command));
    }

    /// <inheritdoc />
    public DangerClassificationResult Classify(ShellCommand command) {
        if (string.IsNullOrWhiteSpace(command.RawCommand))
            return DangerClassificationResult.SafeResult;

        return ClassifyGitEarlyReturn(command)
            ?? CollectSignals(command)
                .Aggregate(new SignalAccumulator([], [], []), AccumulateSignal, BuildResult);
    }

    /// <summary>
    /// git 子命令前置短路 — 只读子命令检查管道/重定向,不可撤回子命令升级为 Execution
    /// </summary>
    private static DangerClassificationResult? ClassifyGitEarlyReturn(ShellCommand command) {
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
    private static IEnumerable<Signal> CollectSignals(ShellCommand command)
        => ClassifyByCommandName(command)
            .Concat(ClassifyByArguments(command))
            .Concat(ClassifyByCombinations(command))
            .Concat(ClassifyByRecurseForce(command));

    /// <summary>
    /// 命令名查表 — 已登记命令返回条目信号,未登记返回 Unknown(黄灯)
    /// </summary>
    private static IEnumerable<Signal> ClassifyByCommandName(ShellCommand command) {
        var entry = MatchCommandEntry(command.CommandName);
        if (entry is not null)
            yield return new(entry.Level, entry.RiskType, $"命令 '{command.CommandName}': {entry.Description}");
        else
            yield return new(CommandDangerLevel.Unknown, CommandRisk.None, $"未知命令 '{command.CommandName}' — 未在 catalog 中登记");
    }

    /// <summary>
    /// 参数检查 — 遍历每个参数,检查危险标志和危险路径
    /// </summary>
    private static IEnumerable<Signal> ClassifyByArguments(ShellCommand command)
        => command.Arguments.SelectMany(CheckArgumentRisk);

    /// <summary>
    /// 单个参数的风险检查 — 危险标志 + 危险路径
    /// </summary>
    private static IEnumerable<Signal> CheckArgumentRisk(string arg) {
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
    /// 管道组合（首模式为 "|"）仍用 AC 扫描原始串，因为管道是 shell 语法结构。
    /// </para>
    /// </summary>
    private static IEnumerable<Signal> ClassifyByCombinations(ShellCommand command) {
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
    private static bool IsCombinationHit(
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
    private static bool CommandNameMatches(string commandNameLower, string pattern)
        => commandNameLower.Equals(pattern, StringComparison.Ordinal)
           || commandNameLower.StartsWith(pattern + ".", StringComparison.Ordinal);

    /// <summary>
    /// 递归+强制组合检查 — Remove-Item/rm/del/erase 的 -Recurse -Force 组合
    /// </summary>
    private static IEnumerable<Signal> ClassifyByRecurseForce(ShellCommand command) {
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
    private static SignalAccumulator AccumulateSignal(SignalAccumulator acc, Signal signal) {
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
    private static DangerClassificationResult BuildResult(SignalAccumulator acc) {
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
    private static DangerousCommandCatalog.CommandEntry? MatchCommandEntry(string commandName) {
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
    /// 判断 git 子命令是否为只读（不修改仓库状态）
    /// </summary>
    private static bool IsGitReadOnlySubcommand(ShellCommand command) {
        if (!command.CommandName.Equals("git", StringComparison.OrdinalIgnoreCase))
            return false;

        // git 无参数 → 只读（显示用法）
        if (command.Arguments.Count == 0)
            return true;

        var subcommand = command.Arguments[0].ToLowerInvariant();

        // 只读子命令白名单
        var readOnlySubcommands = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "status", "log", "diff", "show", "blame", "reflog", "describe", "shortlog",
            "ls-files", "ls-tree", "cat-file", "rev-parse", "rev-list", "name-rev",
            "cherry", "cherry-pick" /* --no-commit 时只读，保守起见不加入 */,
            "branch" /* branch 无 -D/-d 时只读，下面特殊处理 */,
            "remote", "stash" /* stash list 只读，下面特殊处理 */,
            "config" /* config --get 只读，下面特殊处理 */,
            "fetch" /* fetch --dry-run 只读，下面特殊处理 */,
            "grep", "count-objects", "fsck", "gc" /* --auto 时只读 */,
            "help", "version", "var");

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

        return readOnlySubcommands.Contains(subcommand);
    }

    /// <summary>
    /// 判断 git 子命令是否为远程不可撤回操作（升级为 Execution 红灯ask）
    /// git push 推送到远程后无法撤回，git stash drop/tag -d/branch -D 删除操作不可恢复
    /// </summary>
    private static bool IsGitIrreversibleSubcommand(ShellCommand command) {
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
    private static CommandDangerLevel ClassifyPath(string arg) {
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
    /// 检查 Remove-Item/rm/del/erase 的 -Recurse -Force 组合
    /// </summary>
    private static CommandDangerLevel CheckRecurseForceCombination(ShellCommand command) {
        if (!command.CommandName.Equals("Remove-Item", StringComparison.OrdinalIgnoreCase) &&
            !command.CommandName.Equals("rm", StringComparison.OrdinalIgnoreCase) &&
            !command.CommandName.Equals("del", StringComparison.OrdinalIgnoreCase) &&
            !command.CommandName.Equals("erase", StringComparison.OrdinalIgnoreCase))
            return CommandDangerLevel.Safe;

        var hasRecurse = command.Arguments.Any(a =>
            a.Equals("-recurse", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-R", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
            a.Contains('r', StringComparison.OrdinalIgnoreCase) && a.StartsWith('-'));

        var hasForce = command.Arguments.Any(a =>
            a.Equals("-force", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/f", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/q", StringComparison.OrdinalIgnoreCase) ||
            a.Contains('f', StringComparison.OrdinalIgnoreCase) && a.StartsWith('-'));

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
    /// 选择最高优先级的风险类型（用于消息构建）
    /// </summary>
    private static CommandRisk SelectPrimaryRisk(IReadOnlyList<CommandRisk> risks) {
        if (risks.Count == 0)
            return CommandRisk.None;

        var priority = new[]
        {
            CommandRisk.PathEscape,
            CommandRisk.FileDeletion,
            CommandRisk.DirectoryDeletion,
            CommandRisk.PrivilegeEscalation,
            CommandRisk.RemoteExecution,
            CommandRisk.ForceOperation,
            CommandRisk.RecursiveOperation,
            CommandRisk.DataModification,
            CommandRisk.SystemModification,
        };

        foreach (var risk in priority) {
            if (risks.Contains(risk))
                return risk;
        }

        return risks[0];
    }

    /// <summary>
    /// 检查 git 只读命令的管道/重定向 — 管道传入解释器可执行任意代码(Execution),其他管道/重定向需确认(LightValidation)
    /// </summary>
    private static DangerClassificationResult ClassifyGitPipeRedirect(ShellCommand command) {
        if (!command.HasPipe && !command.HasRedirection)
            return DangerClassificationResult.SafeResult;

        if (command.HasPipe && GetPipeTargetCommands(command.Arguments).Any(IsInterpreter)) {
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
    /// 从参数列表中提取所有管道目标命令名(| 后面的第一个参数)
    /// </summary>
    private static IEnumerable<string> GetPipeTargetCommands(IReadOnlyList<string> arguments)
        => Enumerable.Range(0, arguments.Count - 1)
            .Where(i => arguments[i] == "|")
            .Select(i => arguments[i + 1]);

    /// <summary>
    /// 判断命令名是否为解释器(可执行任意代码) — 引用 DangerousCommandCatalog.InterpreterCommands 唯一数据源
    /// </summary>
    private static bool IsInterpreter(string commandName)
        => DangerousCommandCatalog.InterpreterCommands.Contains(commandName);
}