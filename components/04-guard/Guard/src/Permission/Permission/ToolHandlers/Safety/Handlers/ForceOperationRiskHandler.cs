
namespace Core.Permission;

/// <summary>
/// 强制操作风险处理器 — CommandRisk.ForceOperation 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class ForceOperationRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.ForceOperation;

    /// <inheritdoc />
    protected override string OperationName => "强制操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "-f/-force 等强制标志在 Auto 模式下不被允许，如确需执行请切换到 Ask 模式确认";

    /// <inheritdoc />
    protected override string AskModeWarning => "强制操作会跳过安全检查";
}
