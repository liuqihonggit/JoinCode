namespace Api.LLM.QueryServices;

using Api.LLM.QueryServices.Agnes;
using Api.LLM.QueryServices.Anthropic;
using Api.LLM.QueryServices.Azure;
using Api.LLM.QueryServices.OpenAI;

/// <summary>
/// QueryService 工厂 — 按 ProviderKind 分派到对应派生类
/// 这是重构后唯一允许的 switch：构造决策点（非运行时协议分派）
/// 协议差异已下沉到四个派生类（OpenAIQueryService / AzureQueryService / AnthropicQueryService / AgnesQueryService）
/// </summary>
public sealed class QueryServiceFactory : IQueryServiceFactory
{
    public IQueryService Create(
        ProviderConfig config,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 兜底注入：当 ConfigLoader 未注入 Definition 时（如 Dream 组件），使用最小化定义
        // 正常路径由 SettingsMapper / DotEnvConfig / ProviderSetupStep 注入完整 Definition
        if (config.Definition is null)
        {
            config.Definition = new FallbackProviderDefinition(config.Kind);
        }

        // 单一构造分派点 — ProviderKind 由 ProviderConfig.Kind 派生，不再依赖字符串比较
        return config.Kind switch
        {
            ProviderKind.Anthropic => new AnthropicQueryService(config, httpClient, logger, fileSystem),
            ProviderKind.Azure => new AzureQueryService(config, httpClient, logger, fileSystem),
            ProviderKind.Agnes => new AgnesQueryService(config, httpClient, logger, fileSystem),
            // OpenAI / 未知 — 默认走 OpenAI 兼容协议
            _ => new OpenAIQueryService(config, httpClient, logger, fileSystem)
        };
    }
}
