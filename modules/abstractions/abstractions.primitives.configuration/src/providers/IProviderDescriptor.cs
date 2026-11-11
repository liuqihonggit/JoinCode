namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 供应商描述符 — 统一父接口，一个类查询供应商所有属性（TASK054 §7）。
/// <para>LLM 供应商和 Search 供应商都实现此接口（或通过适配器包装），消费方通过 IProviderCatalog 动态检索。</para>
/// <para>线程安全：实现类应为不可变或线程安全（适配器包装的底层对象自身负责线程安全）。</para>
/// </summary>
public interface IProviderDescriptor {
    /// <summary>供应商名称标识（如 "openai"、"tavily"、"sensenova"）— 全局唯一</summary>
    string Name { get; }

    /// <summary>供应商类别 — Llm / Search / Embedding</summary>
    ProviderCategory Category { get; }

    /// <summary>是否支持 OpenAI 兼容格式（openai-compatible 协议）— Search 供应商恒为 false</summary>
    bool SupportsOpenAiFormat { get; }

    /// <summary>是否可用 — API KEY 已配置、端点可达等运行时可用性判断</summary>
    bool IsAvailable { get; }

    /// <summary>显示名（可选，用于 UI 展示；null 时回退 Name）</summary>
    string? DisplayName { get; }
}
