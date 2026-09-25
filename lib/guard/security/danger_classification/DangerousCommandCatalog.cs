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
}