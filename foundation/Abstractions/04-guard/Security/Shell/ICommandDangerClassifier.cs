namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令危险分类器接口 — 统一的命令危险分级入口
/// 替代原 IDestructiveCommandDetector，以 CommandDangerLevel 作为权限决策的唯一依据
/// </summary>
public interface ICommandDangerClassifier
{
    /// <summary>
    /// 分类 Shell 命令的危险等级 — 统一入口，返回 DangerClassificationResult（含 CommandDangerLevel + CommandRisk + 详情）
    /// </summary>
    DangerClassificationResult Classify(ShellCommand command);

    /// <summary>
    /// 分类原始命令字符串的危险等级
    /// </summary>
    DangerClassificationResult Classify(string command);

    /// <summary>
    /// 判断命令是否绝对禁止（Forbidden 级）— AI 不可执行，必须用户手动执行
    /// </summary>
    bool IsForbidden(string command);

    /// <summary>
    /// 获取命令名的危险等级（不解析参数，仅按命令名查表）
    /// </summary>
    CommandDangerLevel GetCommandLevel(string commandName);
}
