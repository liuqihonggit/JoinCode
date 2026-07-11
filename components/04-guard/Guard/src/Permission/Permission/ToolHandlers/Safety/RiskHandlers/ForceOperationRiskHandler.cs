
namespace Core.Permission;

/// <summary>
/// 强制操作风险处理器 — CommandRisk.ForceOperation 的拦截策略
/// Auto 模式: 拒绝 + 提示强制操作风险
/// Ask 模式: 提示确认 + 警告强制操作影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class ForceOperationRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.ForceOperation;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"强制操作已被阻止（{context.Details}）。-f/-force 等强制标志在 Auto 模式下不被允许，如确需执行请切换到 Ask 模式确认";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行强制操作（{context.Details}）。强制操作会跳过安全检查，是否批准？";
    }
}
