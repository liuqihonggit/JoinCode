namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 路径级权限检查器接口 — 对齐 TS checkReadPermissionForTool / checkWritePermissionForTool
/// 检查文件读写操作的路径级权限（工作目录、规则匹配、内部路径白名单等）
/// </summary>
public interface IPathPermissionChecker
{
    /// <summary>
    /// 检查读取权限 — 对齐 TS checkReadPermissionForTool 9步决策链
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>路径权限检查结果</returns>
    PathPermissionCheckResult CheckReadPermission(string path);

    /// <summary>
    /// 检查写入权限 — 对齐 TS checkWritePermissionForTool
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>路径权限检查结果</returns>
    PathPermissionCheckResult CheckWritePermission(string path);

    /// <summary>
    /// 获取 Read deny 规则的排除模式 — 对齐 TS getFileReadIgnorePatterns
    /// 用于将 deny 规则转化为搜索工具的 glob 排除模式
    /// </summary>
    /// <param name="workingDirectory">工作目录，用于规范化相对路径模式</param>
    /// <returns>规范化后的排除模式列表</returns>
    IReadOnlyList<string> GetReadDenyPatterns(string? workingDirectory = null);
}
