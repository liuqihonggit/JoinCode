namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令危险分类器接口 — 统一的命令危险分级入口
/// 4级分级: Safe(只读自动通过) / LightValidation(绿色ask可撤回) / Execution(红色ask不可撤回) / Dangerous(直接拒绝)
/// </summary>
public interface ICommandDangerClassifier
{
    /// <summary>
    /// 分类 Shell 命令的危险等级 — 统一入口，返回 DangerClassificationResult
    /// </summary>
    DangerClassificationResult Classify(ShellCommand command);

    /// <summary>
    /// 分类原始命令字符串的危险等级
    /// </summary>
    DangerClassificationResult Classify(string command);

    /// <summary>
    /// 判断命令是否危险（Dangerous 级）— 直接拒绝不提示
    /// </summary>
    bool IsDangerous(string command);

    /// <summary>
    /// 获取命令名的危险等级（不解析参数，仅按命令名查表）
    /// </summary>
    CommandDangerLevel GetCommandLevel(string commandName);
}
