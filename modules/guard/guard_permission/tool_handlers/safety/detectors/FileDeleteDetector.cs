
namespace Core.Permission;

/// <summary>
/// 文件删除工具检测器 — 检测 file_delete 工具调用
/// </summary>
[Register(typeof(IDeleteOperationDetector), ServiceLifetime.Singleton)]
public sealed partial class FileDeleteDetector : ServiceEntity, IDeleteOperationDetector {
    /// <inheritdoc />
    public bool TryDetect(string toolName, Dictionary<string, JsonElement>? arguments, [NotNullWhen(true)] out DeleteOperationInfo? info) {
        if (!string.Equals(toolName, FileToolNameEnumConstants.FileDelete, StringComparison.OrdinalIgnoreCase)) {
            info = null;
            return false;
        }

        var filePath = PermissionCheckContext.ExtractPathFromArguments(arguments ?? new());

        info = new DeleteOperationInfo {
            TargetPath = filePath,
            SourceDescription = "file_delete 工具"
        };
        return true;
    }
}