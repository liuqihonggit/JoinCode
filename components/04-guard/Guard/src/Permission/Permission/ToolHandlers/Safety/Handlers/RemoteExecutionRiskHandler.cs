
namespace Core.Permission;

/// <summary>
/// 远程执行风险处理器 — CommandRisk.RemoteExecution 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class RemoteExecutionRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.RemoteExecution;

    /// <inheritdoc />
    protected override string OperationName => "远程执行操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "curl/wget 等远程命令在 Auto 模式下不被允许，请使用 WebFetch 工具替代";

    /// <inheritdoc />
    protected override string AskModeWarning => "建议使用 WebFetch 工具替代，是否仍要执行";
}
