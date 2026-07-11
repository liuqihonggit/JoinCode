
namespace Core.Permission;

/// <summary>
/// 系统修改风险处理器 — CommandRisk.SystemModification 的拦截策略
/// Auto 模式: 拒绝 + 提示系统修改风险
/// Ask 模式: 提示确认 + 警告系统修改影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class SystemModificationRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.SystemModification;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"系统修改操作已被阻止（{context.Details}）。此操作可能影响系统配置或稳定性，如确需执行请切换到 Ask 模式确认";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行系统修改操作（{context.Details}）。此操作可能影响系统配置或稳定性，是否批准？";
    }
}
