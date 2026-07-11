
namespace Core.Permission;

/// <summary>
/// 文件删除工具检测器 — 检测 file_delete 工具调用
/// </summary>
[Register(typeof(IDeleteOperationDetector))]
public sealed partial class FileDeleteDetector : IDeleteOperationDetector
{
    /// <inheritdoc />
    public DeleteOperationInfo? Detect(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        if (!string.Equals(toolName, FileToolNameConstants.FileDelete, StringComparison.OrdinalIgnoreCase))
            return null;

        var filePath = PermissionCheckContext.ExtractPathFromArguments(arguments ?? new());

        return new DeleteOperationInfo
        {
            TargetPath = filePath,
            SourceDescription = "file_delete 工具"
        };
    }
}
