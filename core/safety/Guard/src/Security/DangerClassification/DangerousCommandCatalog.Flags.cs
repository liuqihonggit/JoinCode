namespace Core.Security.DangerClassification;

/// <summary>
/// 参数、组合、路径映射表构建 — 危险参数和危险组合的统一定义
/// 绿色ask(LightValidation)=可撤回, 红色ask(Execution)=不可撤回, Dangerous=直接拒绝
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, FlagEntry> BuildFlags()
    {
        var entries = new Dictionary<string, FlagEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // 递归操作 — Execution（不可撤回）
            ["-r"] = new("-r", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),
            ["-R"] = new("-R", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),
            ["/s"] = new("/s", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),
            ["/S"] = new("/S", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),
            ["-recurse"] = new("-recurse", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),
            ["-Recurse"] = new("-Recurse", CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归操作 — 不可撤回"),

            // 强制操作 — Execution（不可撤回）
            ["-f"] = new("-f", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "强制操作 — 不可撤回"),
            ["-force"] = new("-force", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "强制操作 — 不可撤回"),
            ["-Force"] = new("-Force", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "强制操作 — 不可撤回"),
            ["/f"] = new("/f", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "强制操作 — 不可撤回"),
            ["/F"] = new("/F", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "强制操作 — 不可撤回"),
            ["/q"] = new("/q", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "静默模式 — 不可撤回"),
            ["/Q"] = new("/Q", CommandRisk.ForceOperation, CommandDangerLevel.Execution, "静默模式 — 不可撤回"),

            // 根目录/系统盘 — Dangerous（直接拒绝）
            ["/"] = new("/", CommandRisk.PathEscape, CommandDangerLevel.Dangerous, "根目录目标 — 直接拒绝"),
            ["C:\\"] = new("C:\\", CommandRisk.PathEscape, CommandDangerLevel.Dangerous, "系统盘目标 — 直接拒绝"),
            ["C:/"] = new("C:/", CommandRisk.PathEscape, CommandDangerLevel.Dangerous, "系统盘目标 — 直接拒绝"),

            // 通配符删除 — Execution（不可撤回）
            ["*"] = new("*", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "通配符删除 — 不可撤回"),
            ["*."] = new("*.", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "通配符删除 — 不可撤回"),
            ["*.*"] = new("*.*", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "通配符删除 — 不可撤回"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CombinationEntry> BuildCombinations()
    {
        return
        [
            // === Dangerous（直接拒绝）— 格式化系统盘/直接写入块设备/清盘 ===
            // rm -rf / 的检测由 CheckRecurseForceCombination 处理（检查参数是否为根目录）
            new(["format", "c:"], CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "格式化系统盘 — 直接拒绝"),
            new(["dd", "of=", "/dev/"], CommandRisk.DataModification, CommandDangerLevel.Dangerous, "直接写入块设备 — 直接拒绝"),
            new(["diskpart", "clean"], CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "清盘操作 — 直接拒绝"),

            // === Execution（红色 ask / 不可撤回）— 破坏性操作 ===
            new(["del", "/s", "/q"], CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "静默递归删除 — 不可撤回"),
            new(["erase", "/s", "/q"], CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "静默递归删除 — 不可撤回"),
            new(["dd", "of="], CommandRisk.DataModification, CommandDangerLevel.Execution, "直接磁盘写 — 不可撤回"),
            new(["dd", "if="], CommandRisk.DataModification, CommandDangerLevel.Execution, "直接磁盘读写 — 不可撤回"),
            new(["powershell", "-enc"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "编码命令执行 — 不可撤回"),
            new(["powershell", "-encodedcommand"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "编码命令执行 — 不可撤回"),
            new(["pwsh", "-enc"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "编码命令执行 — 不可撤回"),
            new(["|", "sh"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "管道到 shell — 不可撤回"),
            new(["|", "bash"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "管道到 bash — 不可撤回"),
            new(["|", "powershell"], CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "管道到 PowerShell — 不可撤回"),
            new(["git", "reset", "--hard"], CommandRisk.DataModification, CommandDangerLevel.Execution, "破坏性 git reset — 丢失未提交更改，不可撤回"),
            new(["git", "clean", "-f"], CommandRisk.DataModification, CommandDangerLevel.Execution, "强制 git clean — 删除未跟踪文件，不可撤回"),
            new(["chmod", "777"], CommandRisk.SystemModification, CommandDangerLevel.Execution, "世界可写权限 — 不可撤回"),
            new(["shutdown", "/s"], CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统关机 — 不可撤回"),
            new(["rm", "-rf"], CommandRisk.RecursiveOperation, CommandDangerLevel.Execution, "递归强制删除 — 不可撤回"),
            new(["taskkill", "/f"], CommandRisk.DataModification, CommandDangerLevel.Execution, "强制终止进程 — 不可撤回"),
            new(["kill", "-9"], CommandRisk.DataModification, CommandDangerLevel.Execution, "强制终止进程 — 不可撤回"),
        ];
    }

    private static FrozenDictionary<string, CommandDangerLevel> BuildDangerousPaths()
    {
        var entries = new Dictionary<string, CommandDangerLevel>(StringComparer.OrdinalIgnoreCase)
        {
            // Dangerous — 根目录/系统盘/通配符根（直接拒绝）
            ["/"] = CommandDangerLevel.Dangerous,
            ["C:\\"] = CommandDangerLevel.Dangerous,
            ["C:/"] = CommandDangerLevel.Dangerous,
            ["/*"] = CommandDangerLevel.Dangerous,
            ["C:\\*"] = CommandDangerLevel.Dangerous,
            ["C:/*"] = CommandDangerLevel.Dangerous,
            ["/root"] = CommandDangerLevel.Dangerous,

            // Execution — 系统目录（不可撤回）
            ["/home"] = CommandDangerLevel.Execution,
            ["/etc"] = CommandDangerLevel.Execution,
            ["/usr"] = CommandDangerLevel.Execution,
            ["/var"] = CommandDangerLevel.Execution,

            // LightValidation — 用户目录/路径逃逸（可撤回/轻校验）
            ["~"] = CommandDangerLevel.LightValidation,
            ["~/"] = CommandDangerLevel.LightValidation,
            [".."] = CommandDangerLevel.LightValidation,
            ["../"] = CommandDangerLevel.LightValidation,
            ["..\\"] = CommandDangerLevel.LightValidation,
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<CommandRisk, CommandDangerLevel> BuildRiskToLevelMap()
    {
        return new Dictionary<CommandRisk, CommandDangerLevel>
        {
            [CommandRisk.None] = CommandDangerLevel.Safe,
            [CommandRisk.FileDeletion] = CommandDangerLevel.Execution,
            [CommandRisk.DirectoryDeletion] = CommandDangerLevel.Execution,
            [CommandRisk.DataModification] = CommandDangerLevel.Execution,
            [CommandRisk.SystemModification] = CommandDangerLevel.Execution,
            [CommandRisk.PathEscape] = CommandDangerLevel.Dangerous,
            [CommandRisk.RecursiveOperation] = CommandDangerLevel.Execution,
            [CommandRisk.ForceOperation] = CommandDangerLevel.Execution,
            [CommandRisk.RemoteExecution] = CommandDangerLevel.Execution,
            [CommandRisk.PrivilegeEscalation] = CommandDangerLevel.Execution,
            [CommandRisk.ExcessiveSearchScope] = CommandDangerLevel.LightValidation,
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// 根据 CommandRisk 推断默认 CommandDangerLevel（用于无显式等级时的降级推断）
    /// </summary>
    public static CommandDangerLevel InferLevel(CommandRisk risk) => RiskToLevelMap.GetValueOrDefault(risk, CommandDangerLevel.Execution);

    /// <summary>
    /// 合并多个 CommandDangerLevel，取最高危险等级
    /// </summary>
    public static CommandDangerLevel MergeLevels(params CommandDangerLevel[] levels)
    {
        var max = CommandDangerLevel.Safe;
        foreach (var level in levels)
        {
            if (level > max)
                max = level;
        }
        return max;
    }
}
