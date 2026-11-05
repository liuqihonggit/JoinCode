namespace Services.Web.ProviderCatalog;

/// <summary>
/// LLM 供应商描述符适配器 — 包装 IProviderDefinition 为统一 IProviderDescriptor（TASK054 §7）。
/// <para>Category = Llm，SupportsOpenAiFormat = protocol 是 openai-compatible。</para>
/// <para>线程安全：IProviderDefinition 实现自身负责线程安全。</para>
/// </summary>
internal sealed class LlmProviderDescriptor : IProviderDescriptor {
    private readonly IProviderDefinition _definition;

    /// <summary>
    /// 构造 LLM 供应商描述符适配器。
    /// </summary>
    /// <param name="definition">LLM 供应商完整定义</param>
    public LlmProviderDescriptor(IProviderDefinition definition) {
        _definition = definition;
    }

    /// <inheritdoc />
    public string Name => _definition.ProviderName;

    /// <inheritdoc />
    public ProviderCategory Category => ProviderCategory.Llm;

    /// <inheritdoc />
    public bool SupportsOpenAiFormat => _definition.Protocol == ProtocolKind.OpenAiCompatible;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrEmpty(_definition.ResolveApiKeyFromEnv());

    /// <inheritdoc />
    public string? DisplayName => _definition.DisplayName;
}
