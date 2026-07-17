
namespace Core.Permission;

/// <summary>
/// 目录删除风险处理器 — CommandRisk.DirectoryDeletion 的拦截策略
/// Auto 模式: 拒绝 + 引导使用 Move-Item 移动到 .xxx/ 目录
/// Ask 模式: 提示确认 + 建议移动到 .xxx/ 目录
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class DirectoryDeletionRiskHandler : DeletionRiskHandlerBase
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.DirectoryDeletion;

    /// <inheritdoc />
    protected override string OperationName => "目录删除操作";

    /// <inheritdoc />
    protected override string BuildTrashPath(string originalPath)
    {
        var fileName = Path.GetFileName(originalPath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        return $".xxx/{fileName}.{timestamp}.del";
    }
}
