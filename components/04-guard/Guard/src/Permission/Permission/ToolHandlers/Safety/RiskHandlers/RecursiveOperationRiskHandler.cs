
namespace Core.Permission;

/// <summary>
/// 递归操作风险处理器 — CommandRisk.RecursiveOperation 的拦截策略
/// Auto 模式: 拒绝 + 提示递归操作风险
/// Ask 模式: 提示确认 + 警告递归操作影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class RecursiveOperationRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.RecursiveOperation;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"递归操作已被阻止（{context.Details}）。递归操作可能影响大量文件，如确需执行请切换到 Ask 模式确认";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行递归操作（{context.Details}）。递归操作可能影响大量文件，是否批准？";
    }
}
