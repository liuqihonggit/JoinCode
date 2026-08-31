namespace Core.Security.DangerClassification;

/// <summary>
/// 命令危险分类器实现 — 统一的命令危险分级入口，使用 DangerousCommandCatalog 作为唯一数据源
/// 替代原 DestructiveCommandDetector 的检测逻辑，以 CommandDangerLevel 作为权限决策的唯一依据
/// </summary>
[Register(typeof(ICommandDangerClassifier), ServiceLifetime.Singleton)]
public sealed partial class CommandDangerClassifier : ServiceEntity, ICommandDangerClassifier
{
    /// <summary>
    /// AC 自动机 — 展平所有危险组合模式，一次扫描命中全部模式串
    /// </summary>
    private static readonly AhoCorasick<string> CombinationPatternAc = AhoCorasick.Create(
        DangerousCommandCatalog.Combinations.SelectMany(static c => c.LowerPatterns).Distinct(),
        ignoreCase: false);

    /// <inheritdoc />
    public DangerClassificationResult Classify(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return DangerClassificationResult.SafeResult;

        return Classify(ShellCommand.Parse(command));
    }

    /// <inheritdoc />
    public DangerClassificationResult Classify(ShellCommand command)
    {
        var detectedLevels = new List<CommandDangerLevel>();
        var detectedRisks = new List<CommandRisk>();
        var details = new List<string>();

        // 1. 检查命令名是否在危险命令列表中（支持前缀匹配，如 mkfs.ext4 匹配 mkfs）
        var commandEntry = MatchCommandEntry(command.CommandName);
        if (commandEntry is not null)
        {
            detectedLevels.Add(commandEntry.Level);
            detectedRisks.Add(commandEntry.RiskType);
            details.Add($"命令 '{command.CommandName}': {commandEntry.Description}");

            // git 只读子命令降级为 Safe（git status/log/diff 等不修改仓库）
            if (commandEntry.Level == CommandDangerLevel.LightValidation &&
                IsGitReadOnlySubcommand(command))
            {
                return DangerClassificationResult.SafeResult;
            }
        }
        else
        {
            // 未知命令默认 Unknown（黄灯）— 安全原则: 未登记命令需用户确认，防止恶意脚本自动通过
            detectedLevels.Add(CommandDangerLevel.Unknown);
            details.Add($"未知命令 '{command.CommandName}' — 未在 catalog 中登记");
        }

        // 2. 检查危险参数
        foreach (var arg in command.Arguments)
        {
            if (DangerousCommandCatalog.Flags.TryGetValue(arg, out var flagEntry))
            {
                detectedLevels.Add(flagEntry.Level);
                detectedRisks.Add(flagEntry.RiskType);
                details.Add($"危险参数 '{arg}': {flagEntry.Description}");
            }

            // 检查参数中的危险路径
            var pathLevel = ClassifyPath(arg);
            if (pathLevel != CommandDangerLevel.Safe)
            {
                detectedLevels.Add(pathLevel);
                detectedRisks.Add(CommandRisk.PathEscape);
                details.Add($"危险路径参数 '{arg}': {pathLevel}");
            }
        }

        // 3. 检查危险模式组合 — AC 自动机一次扫描命中所有模式，再检查组合
        var rawLower = command.RawCommand.ToLowerInvariant();
        var hitPatterns = new HashSet<string>(
            CombinationPatternAc.FindAll(rawLower.AsSpan()).Select(static m => m.Value),
            StringComparer.Ordinal);
        var matchedCombos = DangerousCommandCatalog.Combinations
            .Where(c => c.LowerPatterns.All(p => hitPatterns.Contains(p)))
            .ToList();
        foreach (var combo in matchedCombos)
        {
            detectedLevels.Add(combo.Level);
            detectedRisks.Add(combo.RiskType);
            details.Add($"危险组合: {combo.Description}");
        }

        // 4. 特殊检查：Remove-Item/rm/del/erase 的 -Recurse -Force 组合（包括 -rf 组合参数）
        var recurseForceLevel = CheckRecurseForceCombination(command);
        if (recurseForceLevel != CommandDangerLevel.Safe)
        {
            detectedLevels.Add(recurseForceLevel);
            detectedRisks.Add(CommandRisk.RecursiveOperation);
            detectedRisks.Add(CommandRisk.ForceOperation);
            details.Add("递归 + 强制组合 — 极度危险");
        }

        // 合并结果：取最高危险等级
        var finalLevel = DangerousCommandCatalog.MergeLevels([.. detectedLevels]);
        var primaryRisk = SelectPrimaryRisk(detectedRisks);
        var detailText = details.Count > 0 ? string.Join("; ", details) : null;

        return new DangerClassificationResult(finalLevel, primaryRisk, detailText);
    }

    /// <inheritdoc />
    public bool IsDangerous(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var classification = Classify(command);
        return classification.IsDangerous;
    }

    /// <inheritdoc />
    public CommandDangerLevel GetCommandLevel(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return CommandDangerLevel.Safe;

        var entry = MatchCommandEntry(commandName);
        return entry?.Level ?? CommandDangerLevel.Unknown;
    }

    /// <summary>
    /// 匹配命令条目（支持前缀匹配，如 mkfs.ext4 匹配 mkfs）
    /// </summary>
    private static DangerousCommandCatalog.CommandEntry? MatchCommandEntry(string commandName)
    {
        if (DangerousCommandCatalog.Commands.TryGetValue(commandName, out var entry))
            return entry;

        // 前缀匹配：mkfs.ext4 → mkfs
        foreach (var (key, value) in DangerousCommandCatalog.Commands)
        {
            if (commandName.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase) ||
                commandName.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>
    /// 判断 git 子命令是否为只读（不修改仓库状态）
    /// </summary>
    private static bool IsGitReadOnlySubcommand(ShellCommand command)
    {
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
        if (subcommand == "branch")
        {
            return !command.Arguments.Any(a =>
                a.Equals("-D", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--delete", StringComparison.OrdinalIgnoreCase));
        }

        // stash list 是只读，其他 stash 操作不是
        if (subcommand == "stash")
        {
            return command.Arguments.Count >= 2 &&
                   command.Arguments[1].Equals("list", StringComparison.OrdinalIgnoreCase);
        }

        // config --get 是只读，config 写入不是
        if (subcommand == "config")
        {
            return command.Arguments.Any(a =>
                a.Equals("--get", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--get-all", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-l", StringComparison.OrdinalIgnoreCase));
        }

        // fetch --dry-run 是只读
        if (subcommand == "fetch")
        {
            return command.Arguments.Any(a =>
                a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        }

        return readOnlySubcommands.Contains(subcommand);
    }

    /// <summary>
    /// 分类路径参数的危险等级
    /// </summary>
    private static CommandDangerLevel ClassifyPath(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return CommandDangerLevel.Safe;

        if (DangerousCommandCatalog.DangerousPaths.TryGetValue(arg, out var level))
            return level;

        foreach (var (path, pathLevel) in DangerousCommandCatalog.DangerousPaths)
        {
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
    private static CommandDangerLevel CheckRecurseForceCombination(ShellCommand command)
    {
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

        if (hasRecurse && hasForce)
        {
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
    private static CommandRisk SelectPrimaryRisk(IReadOnlyList<CommandRisk> risks)
    {
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

        foreach (var risk in priority)
        {
            if (risks.Contains(risk))
                return risk;
        }

        return risks[0];
    }
}
