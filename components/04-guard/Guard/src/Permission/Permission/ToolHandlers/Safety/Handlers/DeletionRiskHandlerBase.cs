
namespace Core.Permission;

/// <summary>
/// 删除操作风险处理器基类 — 提供路径感知的拒绝/确认消息，引导移动到 .xxx/ 目录
/// </summary>
public abstract class DeletionRiskHandlerBase : ICommandRiskHandler
{
    private const string TrashDir = ".xxx";

    /// <inheritdoc />
    public abstract CommandRisk RiskType { get; }

    /// <summary>
    /// 操作名称（如"文件删除"、"目录删除"）
    /// </summary>
    protected abstract string OperationName { get; }

    /// <summary>
    /// 构建回收路径 — 子类可覆盖以自定义路径格式
    /// </summary>
    protected abstract string BuildTrashPath(string originalPath);

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            var trashPath = BuildTrashPath(targetPath);
            return $"{OperationName}已被阻止（{context.Details}）。请使用 Shell 工具移动到 {TrashDir}/ 目录: Move-Item '{targetPath}' '{trashPath}'";
        }

        return $"{OperationName}已被阻止（{context.Details}）。请使用 Shell 工具移动到 {TrashDir}/ 目录";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            return $"工具 '{context.ToolName}' 请求{OperationName} '{targetPath}'（{context.Details}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
        }

        return $"工具 '{context.ToolName}' 请求{OperationName}（{context.Details}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
    }

    /// <summary>
    /// 构建带时间戳的回收文件名
    /// </summary>
    protected static string BuildTimestampedTrashPath(string originalPath, string trashDir)
    {
        var fileName = Path.GetFileName(originalPath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var dotIndex = fileName.IndexOf('.', StringComparison.Ordinal);

        var trashFileName = dotIndex > 0
            ? $"{fileName[..dotIndex]}.{fileName[(dotIndex + 1)..]}.{timestamp}.del"
            : $"{fileName}.{timestamp}.del";

        return Path.Combine(trashDir, trashFileName);
    }
}
