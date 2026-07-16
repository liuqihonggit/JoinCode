
namespace Core.Permission;

/// <summary>
/// 递归操作风险处理器 — CommandRisk.RecursiveOperation 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class RecursiveOperationRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.RecursiveOperation;

    /// <inheritdoc />
    protected override string OperationName => "递归操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "递归操作可能影响大量文件，如确需执行请切换到 Ask 模式确认";

    /// <inheritdoc />
    protected override string AskModeWarning => "递归操作可能影响大量文件";
}
