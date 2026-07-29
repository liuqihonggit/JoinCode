namespace Api.LLM.QueryServices.Agnes;

using Api.LLM.QueryServices.OpenAI;

/// <summary>
/// Agnes AI QueryService — 第四个派生类
/// Agnes 协议与 OpenAI 完全兼容（Bearer Token + chat/completions），
/// 差异（默认 Endpoint / 模型列表 / API Key env var 回退）由 AgnesProviderDefinition 多态在基类处理
/// 此处仅作类型标记，保留独立派生类以匹配"一契约 + 多派生类"结构要求
/// </summary>
public sealed class AgnesQueryService : OpenAIQueryService
{
    public AgnesQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null)
        : base(config, httpClient, logger, fs) { }
}
