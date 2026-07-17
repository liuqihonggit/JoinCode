
namespace Core.Permission;

/// <summary>
/// 文件删除风险处理器 — CommandRisk.FileDeletion 的拦截策略
/// Auto 模式: 拒绝 + 引导使用 Move-Item 移动到 .xxx/ 目录
/// Ask 模式: 提示确认 + 建议移动到 .xxx/ 目录
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class FileDeletionRiskHandler : DeletionRiskHandlerBase
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.FileDeletion;

    /// <inheritdoc />
    protected override string OperationName => "文件删除操作";

    /// <inheritdoc />
    protected override string BuildTrashPath(string originalPath) => BuildTimestampedTrashPath(originalPath, ".xxx");
}
