namespace Core.Security.DangerClassification;

/// <summary>
/// 统一危险命令目录 — 集中所有危险命令、参数、组合的定义，每条记录同时标注 CommandRisk（风险类型）和 CommandDangerLevel（危险等级）
/// 这是权限系统危险指令分级的唯一数据源，替代原 DestructiveCommandDetector 中分散的静态映射表
/// </summary>
public static partial class DangerousCommandCatalog {
    /// <summary>
    /// 解释器命令集合 — 管道传入这些命令可执行任意代码
    /// <para>唯一数据源,供 CommandDangerClassifier.IsInterpreter 和 BuildCombinations 管道组合共用</para>
    /// </summary>
    public static readonly FrozenSet<string> InterpreterCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "bash", "sh", "zsh", "ksh", "dash",
        "python", "python3", "python2",
        "perl", "ruby", "node", "nodejs", "deno",
        "powershell", "pwsh", "cmd");

    /// <summary>
    /// 命令条目 — 描述单个命令的风险类型和危险等级
    /// </summary>
    public sealed record CommandEntry(
        string CommandName,
        CommandRisk RiskType,
        CommandDangerLevel Level,
        string Description);

    /// <summary>
    /// 危险参数条目 — 描述单个参数的风险类型和危险等级
    /// </summary>
    public sealed record FlagEntry(
        string Flag,
        CommandRisk RiskType,
        CommandDangerLevel Level,
        string Description);

    /// <summary>
    /// 危险组合条目 — 描述命令+参数组合的风险类型和危险等级
    /// </summary>
    public sealed record CombinationEntry(
        string[] LowerPatterns,
        CommandRisk RiskType,
        CommandDangerLevel Level,
        string Description);

    /// <summary>
    /// 危险命令模式条目 — 字符串模式 + 描述,供正则模糊匹配消费方共用 — P0-②
    /// </summary>
    public sealed record DangerousPatternEntry(string Pattern, string Description);

    /// <summary>
    /// 命令危险等级映射表 — 命令名 → 条目
    /// 分级原则:
    ///   Forbidden = 整盘/系统级不可逆操作（AI 永远拒绝）
    ///   Critical = 极危险不可逆操作（需显式确认，不可批量批准）
    ///   Dangerous = 危险可引导操作（需确认，引导移动到 .xxx/）
    /// </summary>
    public static readonly FrozenDictionary<string, CommandEntry> Commands = BuildCommands();

    /// <summary>
    /// 危险参数映射表 — 参数 → 条目
    /// </summary>
    public static readonly FrozenDictionary<string, FlagEntry> Flags = BuildFlags();

    /// <summary>
    /// 危险组合列表 — 命令+参数组合 → 条目
    /// </summary>
    public static readonly IReadOnlyList<CombinationEntry> Combinations = BuildCombinations();

    /// <summary>
    /// 危险命令字符串模式 — 用于正则模糊匹配(rm -rf /、format、dd if= 等)
    /// <para>统一数据源,供 AutoModeClassifier 和 PermissionConfig 委托消费 — P0-②</para>
    /// <para>ShellExecutionConfig/DestructiveCommandAnalyzer 因架构层级限制无法引用 Guard,保持独立硬编码</para>
    /// </summary>
    public static readonly string[] DangerousCommandPatterns = BuildDangerousCommandPatterns().Select(e => e.Pattern).ToArray();

    /// <summary>
    /// 危险命令模式条目(含描述) — 供 PermissionConfig 等需要 Description 的消费方使用 — P0-②
    /// </summary>
    public static readonly DangerousPatternEntry[] DangerousPatternEntries = BuildDangerousCommandPatterns();

    /// <summary>
    /// 危险路径集合 — 这些路径作为参数时触发对应危险等级
    /// </summary>
    public static readonly FrozenDictionary<string, CommandDangerLevel> DangerousPaths = BuildDangerousPaths();

    /// <summary>
    /// CommandRisk → 默认 CommandDangerLevel 映射（用于无显式等级时的降级推断）
    /// </summary>
    public static readonly FrozenDictionary<CommandRisk, CommandDangerLevel> RiskToLevelMap = BuildRiskToLevelMap();

    /// <summary>
    /// CommandRisk 优先级数组(从高到低) — 选择主风险的唯一数据源。
    /// <para>
    /// 顺序依据 <see cref="RiskToLevelMap"/>:PathEscape 对应 Dangerous(黑灯直接拒绝),
    /// 其余删除/远程/强制等对应 Execution(红灯 ask)。故 PathEscape 优先级最高。
    /// 消费方:CommandDangerClassifier.SelectPrimaryRisk、DangerousCommandProtectionMiddleware.SelectPrimaryRisk,
    /// 禁止双向维护硬编码数组。
    /// </para>
    /// </summary>
    public static readonly CommandRisk[] RiskPriority = [
        CommandRisk.PathEscape,
        CommandRisk.FileDeletion,
        CommandRisk.DirectoryDeletion,
        CommandRisk.PrivilegeEscalation,
        CommandRisk.RemoteExecution,
        CommandRisk.ForceOperation,
        CommandRisk.RecursiveOperation,
        CommandRisk.DataModification,
        CommandRisk.SystemModification,
    ];

    /// <summary>
    /// 文件删除命令集合 — RiskType == FileDeletion 的命令名派生集合(rm/del/erase/Remove-Item)
    /// <para>唯一数据源,供 CommandDangerClassifier.CheckRecurseForceCombination 委托消费,禁止重复硬编码</para>
    /// </summary>
    public static readonly FrozenSet<string> FileDeletionCommands = Commands.Values
        .Where(static c => c.RiskType == CommandRisk.FileDeletion)
        .Select(static c => c.CommandName)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 全部删除命令集合 — RiskType == FileDeletion 或 DirectoryDeletion 的命令名派生集合(rm/del/erase/Remove-Item/rmdir/rd)
    /// <para>唯一数据源,供 ShellDeleteDetector.DeleteCommandNames 委托消费,禁止重复硬编码</para>
    /// </summary>
    public static readonly FrozenSet<string> AllDeletionCommands = Commands.Values
        .Where(static c => c.RiskType == CommandRisk.FileDeletion || c.RiskType == CommandRisk.DirectoryDeletion)
        .Select(static c => c.CommandName)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 选择最高优先级的风险类型 — 唯一逻辑,消费方委托此方法,禁止重复实现。
    /// <para>空列表返回 <see cref="CommandRisk.None"/>;不在优先级表中的风险返回列表第一个。</para>
    /// </summary>
    /// <param name="risks">风险列表</param>
    /// <returns>最高优先级风险;空列表返回 None</returns>
    public static CommandRisk SelectPrimaryRisk(IReadOnlyList<CommandRisk> risks) {
        if (risks.Count == 0)
            return CommandRisk.None;

        foreach (var risk in RiskPriority) {
            if (risks.Contains(risk))
                return risk;
        }

        return risks[0];
    }
}