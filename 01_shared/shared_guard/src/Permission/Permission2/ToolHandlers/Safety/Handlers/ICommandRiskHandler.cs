
namespace Core.Permission;

/// <summary>
/// 命令风险处理器接口 — 每种 CommandRisk 对应一个处理器，提供细粒度的拦截策略
/// Auto 模式: 根据风险类型返回拒绝/引导消息
/// Ask 模式: 返回待确认，附带风险说明和建议
/// </summary>
public interface ICommandRiskHandler
{
    /// <summary>
    /// 处理器负责的风险类型
    /// </summary>
    CommandRisk RiskType { get; }

    /// <summary>
    /// 构建拦截消息 — Auto/Default 模式下使用
    /// </summary>
    /// <param name="context">风险上下文</param>
    /// <returns>拒绝消息，包含引导提示</returns>
    string BuildRejectionMessage(CommandRiskContext context);

    /// <summary>
    /// 构建确认消息 — Ask 模式下使用
    /// </summary>
    /// <param name="context">风险上下文</param>
    /// <returns>确认消息，包含风险说明和建议</returns>
    string BuildConfirmationMessage(CommandRiskContext context);
}

/// <summary>
/// 命令风险上下文 — 描述检测到的风险操作详情
/// </summary>
public sealed record CommandRiskContext
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Shell 命令（如果是 Shell 操作）
    /// </summary>
    public ShellCommand? ShellCommand { get; init; }

    /// <summary>
    /// 检测到的风险列表
    /// </summary>
    public required IReadOnlyList<CommandRisk> Risks { get; init; }

    /// <summary>
    /// 检测详情
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// 命令中引用的路径
    /// </summary>
    public IReadOnlyList<string> ReferencedPaths => ShellCommand?.ReferencedPaths ?? [];
}
