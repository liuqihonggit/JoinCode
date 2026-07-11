
namespace Core.Permission;

/// <summary>
/// 目录删除风险处理器 — CommandRisk.DirectoryDeletion 的拦截策略
/// Auto 模式: 拒绝 + 引导使用 Move-Item 移动到 .xxx/ 目录
/// Ask 模式: 提示确认 + 建议移动到 .xxx/ 目录
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class DirectoryDeletionRiskHandler : ICommandRiskHandler
{
    private const string TrashDir = ".xxx";

    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.DirectoryDeletion;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var trashPath = $"{TrashDir}/{Path.GetFileName(targetPath)}.{timestamp}.del";
            return $"目录删除操作已被阻止（{context.Details}）。请使用 Shell 工具将目录移动到 {TrashDir}/ 目录: Move-Item '{targetPath}' '{trashPath}'";
        }

        return $"目录删除操作已被阻止（{context.Details}）。请使用 Shell 工具将目录移动到 {TrashDir}/ 目录";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            return $"工具 '{context.ToolName}' 请求删除目录 '{targetPath}'（{context.Details}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
        }

        return $"工具 '{context.ToolName}' 请求删除目录（{context.Details}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
    }
}
