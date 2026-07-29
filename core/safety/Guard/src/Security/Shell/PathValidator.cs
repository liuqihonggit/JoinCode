
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径验证器接口
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// 验证命令中的路径是否都在工作区内
    /// </summary>
    ValidationResult ValidatePaths(ShellCommand command, string workingDirectory);

    /// <summary>
    /// 检查单个路径是否在工作区内
    /// </summary>
    bool IsPathWithinWorkspace(string path, string workingDirectory);
}
