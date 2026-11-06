namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型信息（TASK061 — 1M 上下文时代的模型元数据信息类）— 跨层消费的"当前模型"聚合视图：
/// 模型 ID + 供应商 + 显示名 + 上下文窗口。源头两投影（ModelEntry / ModelItemConfig），
/// <see cref="ValidateOrThrow"/> 强校验（contextWindow&lt;=0 → ConfigurationException 带修复指引 —
/// 主流模型窗口已达 1M，0 值/缺失配置会静默兜底 200K 造成误折叠或透传 0 使下游崩坏，
/// 对齐用户要求"模型配置里写模型名称+上下文大小，不对的直接报错"）。
/// </summary>
public sealed record ModelInfo
{
    /// <summary>模型标识（如 glm-5.2 / sensenova-6.8-flash-lite）。</summary>
    public required string ModelId { get; init; }

    /// <summary>供应商标识（如 zhipu / sensenova）。</summary>
    public required string Provider { get; init; }

    /// <summary>显示名称（如 GLM 5.2）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>上下文窗口大小（token 数 — 校验必须 > 0）。</summary>
    public required int ContextWindow { get; init; }

    /// <summary>从引擎模型条目投影（ModelEntry — IModelCatalog 目录源）。</summary>
    public static ModelInfo FromEntry(string provider, ModelEntry entry) => new() {
        ModelId = entry.Id,
        Provider = provider,
        DisplayName = entry.DisplayName,
        ContextWindow = entry.ContextWindow,
    };

    /// <summary>从配置模型项投影（ModelItemConfig — providers_llm/{vendor}/models.json 源）。</summary>
    public static ModelInfo FromModelItemConfig(string provider, ModelItemConfig config) => new() {
        ModelId = config.Id,
        Provider = provider,
        DisplayName = string.IsNullOrWhiteSpace(config.DisplayName) ? config.Id : config.DisplayName,
        ContextWindow = config.ContextWindow,
    };

    /// <summary>强校验 — contextWindow&lt;=0 抛 ConfigurationException（错误诱导四要素：
    /// 为什么失败/什么配置触发/在哪里修/怎么修）；合法时返回自身（链式）。</summary>
    public ModelInfo ValidateOrThrow() {
        if (ContextWindow <= 0) {
            throw new Exceptions.ConfigurationException(
                $"模型 '{Provider}/{ModelId}' 的 contextWindow 配置无效（{ContextWindow}，必须为正 token 数）。" +
                $"主流模型上下文窗口已达 1M（1048576）；请编辑 ~/.jcc/providers_llm/{Provider}/models.json" +
                $" 为模型 '{ModelId}' 填写正确的 contextWindow 值，" +
                $"或设置环境变量 JCC_MAX_CONTEXT_TOKENS 临时覆盖。",
                configurationKey: $"{Provider}/{ModelId}/contextWindow",
                configurationValue: ContextWindow.ToString());
        }

        return this;
    }
}
