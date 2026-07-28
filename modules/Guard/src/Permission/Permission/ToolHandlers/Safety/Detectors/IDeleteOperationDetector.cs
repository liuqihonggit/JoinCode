
namespace Core.Permission;

/// <summary>
/// 删除操作检测器接口 — 可扩展的删除操作检测机制
/// 实现此接口以支持不同工具类型的删除操作检测（file_delete、Shell rm/del、PowerShell Remove-Item 等）
/// </summary>
public interface IDeleteOperationDetector
{
    /// <summary>
    /// 检测当前工具调用是否为删除操作
    /// </summary>
    /// <returns>删除操作信息，如果不是删除操作则返回 null</returns>
    DeleteOperationInfo? Detect(string toolName, Dictionary<string, JsonElement>? arguments);
}

/// <summary>
/// 删除操作信息 — 描述检测到的删除操作详情
/// </summary>
public sealed record DeleteOperationInfo
{
    /// <summary>
    /// 被删除的目标路径（可能为 null，如 Shell 命令中路径无法提取）
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// 删除操作来源描述（如 "file_delete 工具"、"Shell rm 命令"）
    /// </summary>
    public required string SourceDescription { get; init; }

    /// <summary>
    /// 建议的替代命令（如 "Move-Item 'path' '.xxx/path.timestamp.del'"）
    /// </summary>
    public string? SuggestedAlternative { get; init; }
}
