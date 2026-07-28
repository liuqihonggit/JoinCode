
namespace Core.Permission;

/// <summary>
/// 数据修改风险处理器 — CommandRisk.DataModification 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class DataModificationRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.DataModification;

    /// <inheritdoc />
    protected override string OperationName => "数据修改操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "此操作可能修改或覆盖数据，如确需执行请切换到 Ask 模式确认";

    /// <inheritdoc />
    protected override string AskModeWarning => "此操作可能修改或覆盖数据";
}
