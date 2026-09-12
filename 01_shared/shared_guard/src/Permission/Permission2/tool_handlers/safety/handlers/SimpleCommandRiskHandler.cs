
namespace Core.Permission;

/// <summary>
/// 简单命令风险处理器基类 — 仅需提供风险类型、拒绝模板、确认模板
/// Auto 模式: 拒绝 + 拒绝模板
/// Ask 模式: 确认 + 确认模板
/// </summary>
public abstract class SimpleCommandRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public abstract CommandRisk RiskType { get; }

    /// <summary>
    /// 操作名称（如"强制操作"、"权限提升"等），用于消息模板
    /// </summary>
    protected abstract string OperationName { get; }

    /// <summary>
    /// Auto 模式下的额外提示（如"请切换到 Ask 模式确认"、"请使用 WebFetch 工具替代"等）
    /// </summary>
    protected abstract string AutoModeHint { get; }

    /// <summary>
    /// Ask 模式下的风险说明（如"强制操作会跳过安全检查"、"此操作将以更高权限执行命令"等）
    /// </summary>
    protected abstract string AskModeWarning { get; }

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"{OperationName}已被阻止（{context.Details}）。{AutoModeHint}";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行{OperationName}（{context.Details}）。{AskModeWarning}，是否批准？";
    }
}
