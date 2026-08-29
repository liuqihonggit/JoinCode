namespace Api.LLM.QueryServices;


/// <summary>
/// QueryService 工厂 — 按 ProviderKind 分派到对应派生类
/// 这是重构后唯一允许的 switch：构造决策点（非运行时协议分派）
/// 协议差异已下沉到四个派生类（OpenAIQueryService / AzureQueryService / AnthropicQueryService / AgnesQueryService）
/// </summary>
public sealed class QueryServiceFactory : IQueryServiceFactory
{
    IQueryService IQueryServiceFactory.Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem)
        => Create(config, httpClient, logger, fileSystem, resilientExecutor: null);

    public IQueryService Create(
        ProviderConfig config,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        IFileSystem? fileSystem = null,
        ResilientHttpExecutor? resilientExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 兜底注入：当 ConfigLoader 未注入 Definition 时（如 Dream 组件），使用最小化定义
        // 正常路径由 SettingsMapper / DotEnvConfig / ProviderSetupStep 注入完整 Definition
        if (config.Definition is null)
        {
            config.Definition = new FallbackProviderDefinition(config.ProtocolKind);
        }

        // 单一构造分派点 — 按 ProtocolKind 分派，供应商身份由 Vendor 区分
        return config.ProtocolKind switch
        {
            ProtocolKind.Anthropic => new AnthropicQueryService(config, httpClient, logger, fileSystem, resilientExecutor),
            ProtocolKind.Azure => new AzureQueryService(config, httpClient, logger, fileSystem, resilientExecutor),
            ProtocolKind.Agnes => new AgnesQueryService(config, httpClient, logger, fileSystem, resilientExecutor),
            ProtocolKind.OpenAiResponses => new ResponsesQueryService(config, httpClient, logger, fileSystem, resilientExecutor),
            // OpenAiCompatible / 未知 — 默认走 OpenAI 兼容协议
            _ => new OpenAIQueryService(config, httpClient, logger, fileSystem, resilientExecutor)
        };
    }
}
