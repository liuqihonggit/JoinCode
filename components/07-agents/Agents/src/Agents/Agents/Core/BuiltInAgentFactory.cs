
namespace Core.Agents;

/// <summary>
/// 内置 Agent 工厂 - 创建和管理内置 Agent 实例
/// </summary>
public interface IBuiltInAgentFactory
{
    /// <summary>
    /// 创建指定类型的 Agent
    /// </summary>
    IBuiltInAgent CreateAgent(BuiltInAgentType agentType);

    /// <summary>
    /// 获取所有可用的 Agent 类型
    /// </summary>
    IEnumerable<BuiltInAgentType> GetAvailableAgentTypes();

    /// <summary>
    /// 获取 Agent 配置信息
    /// </summary>
    BuiltInAgentConfig? GetAgentConfig(BuiltInAgentType agentType);
}

/// <summary>
/// 内置 Agent 工厂实现
/// </summary>
[Register]
public sealed class BuiltInAgentFactory : IBuiltInAgentFactory
{
    private readonly IChatClient _kernel;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IContextHierarchy? _contextHierarchy;
    private readonly IContextCompressor? _contextCompressor;
    private readonly ITelemetryService? _telemetryService;
    private readonly Dictionary<BuiltInAgentType, BuiltInAgentConfig> _agentConfigs;

    /// <summary>
    /// 初始化 BuiltInAgentFactory 实例
    /// </summary>
    /// <param name="kernel">LLM 聊天客户端</param>
    /// <param name="fs">文件系统</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="deps">可选依赖聚合（上下文层级、上下文压缩、遥测服务等）</param>
    public BuiltInAgentFactory(
        IChatClient kernel,
        IFileSystem fs,
        IClockService clock,
        AgentFactoryDeps? deps = null)
    {
        _kernel = kernel;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock;
        _loggerFactory = deps?.LoggerFactory;
        _contextHierarchy = deps?.ContextHierarchy;
        _contextCompressor = deps?.ContextCompressor;
        _telemetryService = deps?.TelemetryService;
        _agentConfigs = InitializeAgentConfigs();
    }

    public IBuiltInAgent CreateAgent(BuiltInAgentType agentType)
    {
        _telemetryService?.RecordCount("agent.builtin.creation.count", new Dictionary<string, string> { ["agent_type"] = agentType.ToString() }, "count", "Built-in agent creation count");
        return agentType switch
        {
            BuiltInAgentType.Plan => new PlanAgent(
                _kernel,
                _clock,
                _loggerFactory?.CreateLogger<PlanAgent>()),

            BuiltInAgentType.Explore => new ExploreAgent(
                _kernel,
                _clock,
                _fs,
                _loggerFactory?.CreateLogger<ExploreAgent>()),

            BuiltInAgentType.Verification => new VerificationAgent(
                _kernel,
                _clock,
                _loggerFactory?.CreateLogger<VerificationAgent>()),

            BuiltInAgentType.GeneralPurpose => new GeneralPurposeAgent(
                _kernel,
                _clock,
                _loggerFactory?.CreateLogger<GeneralPurposeAgent>()),

            BuiltInAgentType.ClaudeCodeGuide => new ClaudeCodeGuideAgent(
                _kernel,
                _clock,
                _loggerFactory?.CreateLogger<ClaudeCodeGuideAgent>()),

            BuiltInAgentType.ContextCompression => CreateContextCompressionAgent(),

            _ => throw new ArgumentException($"未知的 Agent 类型: {agentType}", nameof(agentType))
        };
    }

    private IBuiltInAgent CreateContextCompressionAgent()
    {
        if (_contextHierarchy == null || _contextCompressor == null)
        {
            throw new InvalidOperationException(
                "创建 ContextCompressionAgent 需要提供 IContextHierarchy 和 IContextCompressor。" +
                "请通过 AgentFactoryDeps 传入这些依赖。");
        }

        return new ContextCompressionAgent(
            _kernel,
            _clock,
            _contextHierarchy,
            _contextCompressor,
            _loggerFactory?.CreateLogger<ContextCompressionAgent>());
    }

    public IEnumerable<BuiltInAgentType> GetAvailableAgentTypes()
    {
        return _agentConfigs.Keys;
    }

    public BuiltInAgentConfig? GetAgentConfig(BuiltInAgentType agentType)
    {
        return _agentConfigs.TryGetValue(agentType, out var config) ? config : null;
    }

    private static Dictionary<BuiltInAgentType, BuiltInAgentConfig> InitializeAgentConfigs()
    {
        return new Dictionary<BuiltInAgentType, BuiltInAgentConfig>
        {
            [BuiltInAgentType.Plan] = new()
            {
                Name = "PlanAgent",
                Description = "制定清晰、可执行的任务计划，将复杂任务分解为可管理的步骤",
                AgentType = BuiltInAgentType.Plan,
                SystemPrompt = AgentPrompts.PlanAgentSystemPrompt,
                Temperature = LlmParameters.PlanAgent.Temperature,
                MaxTokens = LlmParameters.PlanAgent.MaxTokens
            },
            [BuiltInAgentType.Explore] = new()
            {
                Name = "ExploreAgent",
                Description = "探索代码库结构，识别关键模块和组件，理解代码之间的关系",
                AgentType = BuiltInAgentType.Explore,
                SystemPrompt = AgentPrompts.ExploreAgentSystemPrompt,
                Temperature = LlmParameters.ExploreAgent.Temperature,
                MaxTokens = LlmParameters.ExploreAgent.MaxTokens
            },
            [BuiltInAgentType.Verification] = new()
            {
                Name = "VerificationAgent",
                Description = "验证代码的正确性、质量和安全性，识别潜在问题",
                AgentType = BuiltInAgentType.Verification,
                SystemPrompt = AgentPrompts.VerificationAgentSystemPrompt,
                Temperature = LlmParameters.VerificationAgent.Temperature,
                MaxTokens = LlmParameters.VerificationAgent.MaxTokens
            },
            [BuiltInAgentType.GeneralPurpose] = new()
            {
                Name = "GeneralPurposeAgent",
                Description = "处理各种通用任务，提供信息查询、代码辅助、文本生成等功能",
                AgentType = BuiltInAgentType.GeneralPurpose,
                SystemPrompt = AgentPrompts.GeneralPurposeAgentSystemPrompt,
                Temperature = LlmParameters.GeneralPurposeAgent.Temperature,
                MaxTokens = LlmParameters.GeneralPurposeAgent.MaxTokens
            },
            [BuiltInAgentType.ClaudeCodeGuide] = new()
            {
                Name = "ClaudeCodeGuideAgent",
                Description = "帮助用户了解和使用 Claude Code 的各种功能和最佳实践",
                AgentType = BuiltInAgentType.ClaudeCodeGuide,
                SystemPrompt = AgentPrompts.ClaudeCodeGuideAgentSystemPrompt,
                Temperature = LlmParameters.ClaudeCodeGuideAgent.Temperature,
                MaxTokens = LlmParameters.ClaudeCodeGuideAgent.MaxTokens
            },
            [BuiltInAgentType.ContextCompression] = new()
            {
                Name = "ContextCompressionAgent",
                Description = "智能压缩和管理上下文，优化 Token 使用并保留关键信息",
                AgentType = BuiltInAgentType.ContextCompression,
                SystemPrompt = AgentPrompts.ContextCompressionAgentSystemPrompt,
                Temperature = LlmParameters.ContextCompressionAgent.Temperature,
                MaxTokens = LlmParameters.ContextCompressionAgent.MaxTokens
            }
        };
    }
}
