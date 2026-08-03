
using JoinCode.Abstractions.Interfaces;

namespace Core.Configuration.Providers;

/// <summary>
/// 上下文窗口大小解析器 — 对齐 TS getContextWindowForModel 纯函数式设计
/// 每次调用实时解析当前模型的上下文窗口大小，不缓存状态
/// </summary>
[Register]
public sealed partial class ContextWindowResolver : IContextWindowResolver
{
    private readonly IFastModeService _fastModeService;
    private readonly IProviderDefinitionRegistry _registry;
    private readonly WorkflowConfig? _config;

    private const int DefaultContextWindow = 200_000;

    public ContextWindowResolver(IFastModeService fastModeService, IProviderDefinitionRegistry registry, WorkflowConfig? config = null)
    {
        _fastModeService = fastModeService;
        _registry = registry;
        _config = config;
    }

    /// <summary>
    /// 解析当前模型的上下文窗口大小
    /// 优先级链（对齐 TS getContextWindowForModel）：
    /// 1. 环境变量覆盖
    /// 2. Provider 定义中的模型匹配
    /// 3. 默认值 200K
    /// </summary>
    public int ResolveCurrentContextWindow()
    {
        // 1. 环境变量覆盖（对齐 TS CLAUDE_CODE_MAX_CONTEXT_TOKENS）
        var envOverride = Environment.GetEnvironmentVariable("JCC_MAX_CONTEXT_TOKENS");
        if (!string.IsNullOrWhiteSpace(envOverride) && int.TryParse(envOverride, out var envValue) && envValue > 0)
            return envValue;

        // 2. 从 Provider 定义中查找当前模型
        var currentModel = ResolveCurrentModelId();
        var provider = ResolveCurrentProvider();

        var definition = _registry.TryGet(provider);
        if (definition is not null)
        {
            var match = definition.AvailableModels
                .FirstOrDefault(m => m.Id.Equals(currentModel, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.ContextWindow;
        }

        // 3. 默认值
        return DefaultContextWindow;
    }

    private string ResolveCurrentModelId()
    {
        // FastMode 激活时使用 FastModel，否则使用 PrimaryModel
        if (_fastModeService.IsFastModeActive && !string.IsNullOrWhiteSpace(_fastModeService.FastModelId))
            return _fastModeService.FastModelId;

        return _fastModeService.PrimaryModelId;
    }

    private string ResolveCurrentProvider()
    {
        return _config?.Provider?.Provider
            ?? Environment.GetEnvironmentVariable("JCC_PROVIDER")
            ?? "openai";
    }
}
