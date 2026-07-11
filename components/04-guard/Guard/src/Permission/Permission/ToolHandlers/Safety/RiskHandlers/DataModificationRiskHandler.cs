
namespace Core.Permission;

/// <summary>
/// 数据修改风险处理器 — CommandRisk.DataModification 的拦截策略
/// Auto 模式: 拒绝 + 提示数据修改风险
/// Ask 模式: 提示确认 + 警告数据修改影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class DataModificationRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.DataModification;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"数据修改操作已被阻止（{context.Details}）。此操作可能修改或覆盖数据，如确需执行请切换到 Ask 模式确认";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行数据修改操作（{context.Details}）。此操作可能修改或覆盖数据，是否批准？";
    }
}
