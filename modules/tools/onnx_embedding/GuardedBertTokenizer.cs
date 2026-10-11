namespace OnnxEmbedding;

/// <summary>
/// BertTokenizer 守卫装饰器 — 拦截 considerPreTokenization=false 退化调用。
/// <para>守卫层: PreTokenizationGuard (Priority=100)</para>
/// <para>拦截: considerPreTokenization=false → 所有词被当 [UNK]，向量全同，Score 全 1.0</para>
/// <para>设计理由: 防止未来误改参数导致语义搜索静默失效，错误必须响亮且带诱导方式。</para>
/// </summary>
public sealed class GuardedBertTokenizer {

    private readonly BertTokenizer _inner;

    /// <summary>守卫名称 — 供审计日志和错误追踪定位拦截层。</summary>
    public string Name => "PreTokenizationGuard";

    /// <summary>守卫优先级 — 纵深防御链中越早拦截越好，100=tokenizer 层。</summary>
    public int Priority => 100;

    /// <summary>
    /// 构造守卫装饰器 — 包装 BertTokenizer 实例。
    /// </summary>
    /// <param name="inner">被包装的 BertTokenizer（由 BertTokenizer.Create 创建）。</param>
    public GuardedBertTokenizer(BertTokenizer inner) {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// 安全文档编码 — 强制 considerPreTokenization=true，调用方无需关心危险参数。
    /// <para>推荐用法：OnnxEmbedder 内部调用此方法，消除参数误传风险。</para>
    /// </summary>
    /// <param name="text">待编码文本。</param>
    /// <param name="addSpecialTokens">是否添加 [CLS]/[SEP]（默认 true）。</param>
    /// <returns>token ID 列表。</returns>
    public IReadOnlyList<int> EncodeToIdsSafe(string text, bool addSpecialTokens = true) {
        return _inner.EncodeToIds(text,
            addSpecialTokens: addSpecialTokens,
            considerPreTokenization: true,
            considerNormalization: false);
    }

    /// <summary>
    /// 编码 — 拦截 considerPreTokenization=false，抛出带诱导方式的报错。
    /// <para>守卫触发时报错包含 4 要素：①为什么拒绝 ②触发参数 ③拦截守卫 ④正确做法。</para>
    /// </summary>
    /// <param name="text">待编码文本。</param>
    /// <param name="addSpecialTokens">是否添加 [CLS]/[SEP]。</param>
    /// <param name="considerPreTokenization">是否预分词 — false 会被拦截。</param>
    /// <param name="considerNormalization">是否归一化。</param>
    /// <returns>token ID 列表。</returns>
    /// <exception cref="ArgumentException">considerPreTokenization=false 时抛出。</exception>
    public IReadOnlyList<int> EncodeToIds(
        string text, bool addSpecialTokens,
        bool considerPreTokenization, bool considerNormalization) {

        if (!considerPreTokenization) {
            ThrowPreTokenizationGuardRejected();
        }
        return _inner.EncodeToIds(text, addSpecialTokens, considerPreTokenization, considerNormalization);
    }

    /// <summary>
    /// 批量安全编码 — 强制 considerPreTokenization=true，适合 OnnxEmbedder.EmbedBatch 调用。
    /// </summary>
    /// <param name="texts">待编码文本列表。</param>
    /// <param name="addSpecialTokens">是否添加 [CLS]/[SEP]（默认 true）。</param>
    /// <returns>每条文本的 token ID 列表。</returns>
    public IReadOnlyList<IReadOnlyList<int>> EncodeBatchSafe(IReadOnlyList<string> texts, bool addSpecialTokens = true) {
        var results = new IReadOnlyList<int>[texts.Count];
        for (var i = 0; i < texts.Count; i++) {
            results[i] = EncodeToIdsSafe(texts[i], addSpecialTokens);
        }
        return results;
    }

    [DoesNotReturn]
    private static void ThrowPreTokenizationGuardRejected() {
        throw new ArgumentException(
            """
            ┌─────────────────────────────────────────────────────────────┐
            │ PreTokenizationGuard 拦截: considerPreTokenization=false   │
            └─────────────────────────────────────────────────────────────┘

            ① 为什么拒绝:
               considerPreTokenization=false 跳过 BERT 预分词，
               所有词被当作 [UNK] (id=100) 处理，
               不同文本编码为相同的 [CLS][UNK][SEP] (101,100,102)，
               ONNX 推理产生完全相同的向量，
               余弦相似度全 1.0000，语义搜索完全失效。

                 _tokenizer.EncodeToIds(text, addSpecialTokens: true,
                     considerPreTokenization: false,
            ────────────────────────────^────
                     considerNormalization: false)
                                        ^^^^^
                 退化: "hello world" → [101, 100, 102]
                        "using System;" → [101, 100, 102]  ← 完全相同!

            ② 触发参数:
               considerPreTokenization: false

            ③ 拦截守卫:
               GuardedBertTokenizer.PreTokenizationGuard (Priority=100)

            ④ 正确做法:
               ✅ considerPreTokenization: true
               ✅ 或调用 EncodeToIdsSafe(text) — 自动强制正确参数，无需传 considerPreTokenization
            """);
    }
}
