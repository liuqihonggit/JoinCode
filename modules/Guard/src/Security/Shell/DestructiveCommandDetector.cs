
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 破坏性命令检测器接口
/// </summary>
public interface IDestructiveCommandDetector
{
    /// <summary>
    /// 检测命令是否为破坏性命令
    /// </summary>
    DestructiveCommandResult Detect(ShellCommand command);
}
