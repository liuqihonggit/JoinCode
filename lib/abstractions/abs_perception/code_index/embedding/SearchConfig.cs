namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 语义搜索默认配置 — 统一管理超参数，避免零散硬编码。
/// <para>过采样倍数: top_k × 5，让更多候选进入重排序。</para>
/// <para>重排序权重: 向量余弦(0.35) + 关键词重叠(0.25) + 符号名匹配(0.25) + 文件名匹配(0.15)。</para>
/// <para>附加信号: 精确符号名匹配 boost(+0.10) + 测试文件 penalty(-0.05) + 接口文件 boost(+0.05)。</para>
/// <para>图谱加权: 调用关系(+0.05) + 同文件(+0.03) + 同命名空间(+0.02)。</para>
/// </summary>
public static class SearchConfig {
    /// <summary>默认 top_k — 搜索结果数量。</summary>
    public const int DefaultTopK = 20;

    /// <summary>过采样倍数 — oversampleK = top_k × OversampleFactor。</summary>
    public const int OversampleFactor = 5;

    /// <summary>过采样最小增量 — oversampleK = max(top_k × OversampleFactor, top_k + OversampleMinOffset)。</summary>
    public const int OversampleMinOffset = 20;

    /// <summary>向量余弦权重。</summary>
    public const float VectorWeight = 0.35f;

    /// <summary>关键词重叠权重。</summary>
    public const float KeywordWeight = 0.25f;

    /// <summary>符号名匹配权重。</summary>
    public const float SymbolWeight = 0.25f;

    /// <summary>文件名匹配权重。</summary>
    public const float FileNameWeight = 0.15f;

    /// <summary>图谱加权 — 调用关系。</summary>
    public const float GraphCallWeight = 0.05f;

    /// <summary>图谱加权 — 同文件。</summary>
    public const float GraphSameFileWeight = 0.03f;

    /// <summary>图谱加权 — 同命名空间。</summary>
    public const float GraphSameNamespaceWeight = 0.02f;

    /// <summary>精确符号名匹配 boost — 查询完全匹配符号名时加分。</summary>
    public const float ExactSymbolMatchBoost = 0.10f;

    /// <summary>测试文件 penalty — 测试文件减分（源码优先）。</summary>
    public const float TestFilePenalty = 0.05f;

    /// <summary>接口文件 boost — 接口定义文件加分（抽象优先于实现）。</summary>
    public const float InterfaceFileBoost = 0.05f;

    /// <summary>符号索引注入候选的初始分数 — 中等值，让重排序 boost 接管最终排名。</summary>
    public const float SymbolInjectionScore = 0.50f;

    /// <summary>符号索引精确匹配注入分数 — 高值，确保精确匹配进入候选集前列。</summary>
    public const float SymbolExactInjectionScore = 0.75f;

    /// <summary>计算过采样 K 值。</summary>
    public static int ComputeOversampleK(int topK) =>
        Math.Max(topK * OversampleFactor, topK + OversampleMinOffset);
}
