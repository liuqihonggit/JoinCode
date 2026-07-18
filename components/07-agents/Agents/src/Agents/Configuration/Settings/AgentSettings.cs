
namespace Core.Agents.Configuration.Settings;

/// <summary>
/// Agent 配置设置
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// 最大并发 Agent 数
    /// </summary>
    public int MaxConcurrentAgents { get; set; } = 10;

    /// <summary>
    /// Agent 超时时间（秒）
    /// </summary>
    public int AgentTimeoutSeconds { get; set; } = WorkflowConstants.Timeouts.AgentTimeoutSeconds;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 是否启用工作树隔离
    /// </summary>
    public bool EnableWorktreeIsolation { get; set; } = true;

    /// <summary>
    /// 默认模型名称
    /// </summary>
    public string DefaultModelName { get; set; } = ModelConfigLoader.GetDefaultModelId("deepseek");

    /// <summary>
    /// 最大上下文长度
    /// </summary>
    public int MaxContextLength { get; set; } = 128000;
}
