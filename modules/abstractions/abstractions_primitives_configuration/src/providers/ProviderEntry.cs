namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 供应商条目 — 不可变类，包含完整连接信息 + 模型列表 + 当前模型。
/// <para>引用类型，挂在 ProviderCatalog 注册表上，传递指针不传值。</para>
/// <para>热更新时由 ProviderCatalog 原子替换整个 map，不修改单个 ProviderEntry。</para>
/// </summary>
public sealed class ProviderEntry {
    /// <summary>供应商名称标识（如 settings.json vendor 字典的键）</summary>
    public string Name { get; }

    /// <summary>API 接入点 URL</summary>
    public string Endpoint { get; }

    /// <summary>协议类型（openai-compatible / anthropic / azure 等）</summary>
    public string Protocol { get; }

    /// <summary>API Key 环境变量名（如 OPENAI_API_KEY）</summary>
    public string? ApiKeyEnvVar { get; }

    /// <summary>当前选中的模型 — null 表示未选择</summary>
    public ModelEntry? CurrentModel { get; internal set; }

    /// <summary>该供应商可用的模型列表</summary>
    public IReadOnlyList<ModelEntry> Models { get; }

    /// <summary>构造供应商条目</summary>
    /// <param name="name">供应商名称标识</param>
    /// <param name="endpoint">API 接入点 URL</param>
    /// <param name="protocol">协议类型</param>
    /// <param name="apiKeyEnvVar">API Key 环境变量名</param>
    /// <param name="currentModel">当前选中的模型</param>
    /// <param name="models">可用模型列表</param>
    public ProviderEntry(
        string name,
        string endpoint,
        string protocol,
        string? apiKeyEnvVar,
        ModelEntry? currentModel,
        IReadOnlyList<ModelEntry> models) {
        Name = name;
        Endpoint = endpoint;
        Protocol = protocol;
        ApiKeyEnvVar = apiKeyEnvVar;
        CurrentModel = currentModel;
        Models = models;
    }
}
