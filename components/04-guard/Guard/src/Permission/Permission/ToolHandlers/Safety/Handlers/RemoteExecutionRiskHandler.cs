
namespace Core.Permission;

/// <summary>
/// 远程执行风险处理器 — CommandRisk.RemoteExecution 的拦截策略
/// Auto 模式: 拒绝 + 提示远程执行风险
/// Ask 模式: 提示确认 + 警告远程执行影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class RemoteExecutionRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.RemoteExecution;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"远程执行操作已被阻止（{context.Details}）。curl/wget 等远程命令在 Auto 模式下不被允许，请使用 WebFetch 工具替代";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行远程操作（{context.Details}）。建议使用 WebFetch 工具替代，是否仍要执行？";
    }
}
