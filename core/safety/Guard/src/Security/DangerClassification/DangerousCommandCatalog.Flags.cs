namespace Core.Security.DangerClassification;

/// <summary>
/// 参数、组合、路径映射表构建 — 危险参数和危险组合的统一定义
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, FlagEntry> BuildFlags()
    {
        var entries = new Dictionary<string, FlagEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // 递归操作 — Dangerous（单独使用需确认）
            ["-r"] = new("-r", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),
            ["-R"] = new("-R", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),
            ["/s"] = new("/s", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),
            ["/S"] = new("/S", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),
            ["-recurse"] = new("-recurse", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),
            ["-Recurse"] = new("-Recurse", CommandRisk.RecursiveOperation, CommandDangerLevel.Dangerous, "递归操作"),

            // 强制操作 — Dangerous（单独使用需确认）
            ["-f"] = new("-f", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "强制操作"),
            ["-force"] = new("-force", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "强制操作"),
            ["-Force"] = new("-Force", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "强制操作"),
            ["/f"] = new("/f", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "强制操作"),
            ["/F"] = new("/F", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "强制操作"),
            ["/q"] = new("/q", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "静默模式（无确认）"),
            ["/Q"] = new("/Q", CommandRisk.ForceOperation, CommandDangerLevel.Dangerous, "静默模式（无确认）"),

            // 根目录/系统盘 — Forbidden（绝对禁止）
            ["/"] = new("/", CommandRisk.PathEscape, CommandDangerLevel.Forbidden, "根目录目标 — 绝对禁止"),
            ["C:\\"] = new("C:\\", CommandRisk.PathEscape, CommandDangerLevel.Forbidden, "系统盘目标 — 绝对禁止"),
            ["C:/"] = new("C:/", CommandRisk.PathEscape, CommandDangerLevel.Forbidden, "系统盘目标 — 绝对禁止"),

            // 通配符删除 — Dangerous
            ["*"] = new("*", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "通配符删除"),
            ["*."] = new("*.", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "通配符删除"),
            ["*.*"] = new("*.*", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "通配符删除"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CombinationEntry> BuildCombinations()
    {
        return
        [
            // === Forbidden（绝对禁止）— 格式化系统盘/直接写入块设备/清盘 ===
            // 注意：rm -rf / 的检测由 CheckRecurseForceCombination 处理（检查参数是否为根目录），
            // 不在此处用 AC 自动机子串匹配，避免 /tmp/path 中的 / 被误匹配为根目录
            new(["format", "c:"], CommandRisk.SystemModification, CommandDangerLevel.Forbidden, "格式化系统盘 — 绝对禁止"),
            new(["dd", "of=", "/dev/"], CommandRisk.DataModification, CommandDangerLevel.Forbidden, "直接写入块设备 — 绝对禁止"),
            new(["diskpart", "clean"], CommandRisk.SystemModification, CommandDangerLevel.Forbidden, "清盘操作 — 绝对禁止"),

            // === Critical（极危险）— 不可逆操作 ===
            new(["del", "/s", "/q"], CommandRisk.RecursiveOperation, CommandDangerLevel.Critical, "静默递归删除 — 不可逆"),
            new(["erase", "/s", "/q"], CommandRisk.RecursiveOperation, CommandDangerLevel.Critical, "静默递归删除 — 不可逆"),
            new(["dd", "of="], CommandRisk.DataModification, CommandDangerLevel.Critical, "直接磁盘写 — 不可逆"),
            new(["dd", "if="], CommandRisk.DataModification, CommandDangerLevel.Critical, "直接磁盘读写 — 不可逆"),
            new(["powershell", "-enc"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "编码命令执行 — 可能隐藏恶意代码"),
            new(["powershell", "-encodedcommand"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "编码命令执行 — 可能隐藏恶意代码"),
            new(["pwsh", "-enc"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "编码命令执行 — 可能隐藏恶意代码"),
            new(["|", "sh"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "管道到 shell — 可能执行任意命令"),
            new(["|", "bash"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "管道到 bash — 可能执行任意命令"),
            new(["|", "powershell"], CommandRisk.RemoteExecution, CommandDangerLevel.Critical, "管道到 PowerShell — 可能执行任意命令"),
            new(["git", "reset", "--hard"], CommandRisk.DataModification, CommandDangerLevel.Critical, "破坏性 git reset — 丢失未提交更改"),
            new(["chmod", "777"], CommandRisk.SystemModification, CommandDangerLevel.Critical, "世界可写权限 — 安全风险"),
            new(["shutdown", "/s"], CommandRisk.SystemModification, CommandDangerLevel.Critical, "系统关机 — 不可逆"),
            new(["rm", "-rf"], CommandRisk.RecursiveOperation, CommandDangerLevel.Critical, "递归强制删除 — 不可逆"),

            // === Dangerous（危险）— 需确认 ===
            new(["git", "clean", "-f"], CommandRisk.DataModification, CommandDangerLevel.Dangerous, "强制 git clean — 删除未跟踪文件"),
            new(["taskkill", "/f"], CommandRisk.DataModification, CommandDangerLevel.Dangerous, "强制终止进程"),
            new(["kill", "-9"], CommandRisk.DataModification, CommandDangerLevel.Dangerous, "强制终止进程"),
        ];
    }

    private static FrozenDictionary<string, CommandDangerLevel> BuildDangerousPaths()
    {
        var entries = new Dictionary<string, CommandDangerLevel>(StringComparer.OrdinalIgnoreCase)
        {
            // Forbidden — 根目录/系统盘/通配符根
            ["/"] = CommandDangerLevel.Forbidden,
            ["C:\\"] = CommandDangerLevel.Forbidden,
            ["C:/"] = CommandDangerLevel.Forbidden,
            ["/*"] = CommandDangerLevel.Forbidden,
            ["C:\\*"] = CommandDangerLevel.Forbidden,
            ["C:/*"] = CommandDangerLevel.Forbidden,

            // Critical — 系统目录
            ["/home"] = CommandDangerLevel.Critical,
            ["/root"] = CommandDangerLevel.Forbidden,
            ["/etc"] = CommandDangerLevel.Critical,
            ["/usr"] = CommandDangerLevel.Critical,
            ["/var"] = CommandDangerLevel.Critical,

            // Dangerous — 用户目录/路径逃逸
            ["~"] = CommandDangerLevel.Dangerous,
            ["~/"] = CommandDangerLevel.Dangerous,
            [".."] = CommandDangerLevel.Dangerous,
            ["../"] = CommandDangerLevel.Dangerous,
            ["..\\"] = CommandDangerLevel.Dangerous,
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<CommandRisk, CommandDangerLevel> BuildRiskToLevelMap()
    {
        return new Dictionary<CommandRisk, CommandDangerLevel>
        {
            [CommandRisk.None] = CommandDangerLevel.Safe,
            [CommandRisk.FileDeletion] = CommandDangerLevel.Dangerous,
            [CommandRisk.DirectoryDeletion] = CommandDangerLevel.Dangerous,
            [CommandRisk.DataModification] = CommandDangerLevel.Dangerous,
            [CommandRisk.SystemModification] = CommandDangerLevel.Critical,
            [CommandRisk.PathEscape] = CommandDangerLevel.Forbidden,
            [CommandRisk.RecursiveOperation] = CommandDangerLevel.Dangerous,
            [CommandRisk.ForceOperation] = CommandDangerLevel.Dangerous,
            [CommandRisk.RemoteExecution] = CommandDangerLevel.Dangerous,
            [CommandRisk.PrivilegeEscalation] = CommandDangerLevel.Dangerous,
            [CommandRisk.ExcessiveSearchScope] = CommandDangerLevel.Dangerous,
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// 根据 CommandRisk 推断默认 CommandDangerLevel（用于无显式等级时的降级推断）
    /// </summary>
    public static CommandDangerLevel InferLevel(CommandRisk risk) => RiskToLevelMap.GetValueOrDefault(risk, CommandDangerLevel.Dangerous);

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
