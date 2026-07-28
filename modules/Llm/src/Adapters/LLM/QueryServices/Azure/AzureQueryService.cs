namespace Api.LLM.QueryServices.Azure;

using Api.LLM.QueryServices.OpenAI;

/// <summary>
/// Azure OpenAI QueryService 实现 — 协议层与 OpenAI 兼容（chat/completions 端点）
/// Azure 与 OpenAI 的差异（api-key Header / 部署路径 URL / api-version 查询参数）已通过
/// AzureProviderDefinition 在基类多态处理。本派生类作为类型标记存在，便于工厂按 ProviderKind 显式分发。
/// 后续若 Azure 引入独立协议行为（如 OAuth Token 注入），可在此覆写。
/// </summary>
public sealed class AzureQueryService : OpenAIQueryService
{
    public AzureQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null)
        : base(config, httpClient, logger, fs)
    {
        // 协议层完全继承 OpenAI 实现；URL/端点/认证差异由 AzureProviderDefinition 多态注入
    }
}
