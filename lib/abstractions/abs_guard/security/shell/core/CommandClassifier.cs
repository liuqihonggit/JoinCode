namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 命令分类器 - 对命令进行语义分析和分类
/// </summary>
public interface ICommandClassifier
{
    /// <summary>
    /// 对命令进行分类
    /// </summary>
    CommandClassification Classify(ShellCommand command, string workingDirectory);
}

/// <summary>
/// 破坏性命令检测结果
/// </summary>
public sealed record DestructiveCommandResult(
    bool IsDestructive,
    IReadOnlyList<CommandRisk> Risks,
    string? Details = null);
