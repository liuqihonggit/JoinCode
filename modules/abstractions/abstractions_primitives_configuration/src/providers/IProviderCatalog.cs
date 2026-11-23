namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 供应商目录 — 动态检索所有已注册供应商（TASK054 §7）。
/// <para>从 DI 收集所有 IProviderDescriptor（LLM 适配器 + Search 适配器），运行时动态构建，不硬编码。</para>
/// <para>线程安全：构建后只读，FrozenDictionary/FrozenList 自身线程安全。</para>
/// </summary>
public interface IProviderCatalog {
    /// <summary>
    /// 获取所有已注册供应商描述符（LLM + Search + Embedding + ...）。
    /// </summary>
    IReadOnlyList<IProviderDescriptor> GetAll();

    /// <summary>
    /// 按类别筛选供应商。
    /// </summary>
    /// <param name="category">供应商类别（Llm / Search / Embedding）</param>
    /// <returns>该类别下所有供应商描述符</returns>
    IReadOnlyList<IProviderDescriptor> GetByCategory(ProviderCategory category);

    /// <summary>
    /// 按名称查找供应商。
    /// </summary>
    /// <param name="name">供应商名称</param>
    /// <param name="descriptor">命中时为供应商描述符（非 null）</param>
    /// <returns>命中返回 true；未找到返回 false</returns>
    bool TryGet(string name, [NotNullWhen(true)] out IProviderDescriptor? descriptor);
}
