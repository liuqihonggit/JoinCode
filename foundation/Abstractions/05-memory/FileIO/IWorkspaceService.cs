
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工作区服务接口 - 管理会话级额外工作目录
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// 添加额外工作目录
    /// </summary>
    bool AddDirectory(string path);

    /// <summary>
    /// 移除额外工作目录
    /// </summary>
    bool RemoveDirectory(string path);

    /// <summary>
    /// 获取所有额外工作目录
    /// </summary>
    IReadOnlyList<string> GetAdditionalDirectories();

    /// <summary>
    /// 清除所有额外工作目录
    /// </summary>
    void Clear();
}
