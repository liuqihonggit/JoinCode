namespace JoinCode.Abstractions.Interfaces.Security;

/// <summary>
/// 信任目录管理接口 - 管理工作区信任目录的增删查
/// </summary>
public interface ITrustFolderManager
{
    /// <summary>
    /// 检查目录是否已信任
    /// </summary>
    bool IsTrusted(string folderPath);

    /// <summary>
    /// 添加信任目录
    /// </summary>
    void Trust(string folderPath);

    /// <summary>
    /// 移除信任目录
    /// </summary>
    void Untrust(string folderPath);

    /// <summary>
    /// 获取所有信任目录
    /// </summary>
    IReadOnlyList<string> GetAllTrustedFolders();

    /// <summary>
    /// 清除所有信任目录
    /// </summary>
    void ClearAll();
}
