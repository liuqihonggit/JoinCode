namespace JoinCode.CodeIndex.Vector;

/// <summary>
/// 暴力搜索 ANN 实现 — 遍历全部向量计算余弦相似度，取 top-K。
/// <para>起步方案：零依赖、AOT 友好、简单可靠。</para>
/// <para>适用 &lt;10K 块的中小代码库（实测 0.969ms/10K×256维）。</para>
/// <para>线程安全：无锁实现，volatile ImmutableHamT + Interlocked.CompareExchange CAS 循环。读无锁 O(1)，写 CAS 替换整个映射。</para>
/// </summary>
public sealed class BruteForceAnn : IAnnSearch {

    private volatile ImmutableHamT<string, float[]> _vectors = ImmutableHamT<string, float[]>.Empty;

    /// <summary>当前向量数量。</summary>
    public int Count => _vectors.Count;

    /// <summary>添加单个向量。若 id 已存在则覆盖。</summary>
    public void Add(string id, ReadOnlySpan<float> vector) {
        ArgumentNullException.ThrowIfNull(id);
        var arr = vector.ToArray();
        while (true) {
            var snapshot = _vectors;
            if (Interlocked.CompareExchange(ref _vectors, snapshot.SetItem(id, arr), snapshot) == snapshot) return;
        }
    }

    /// <summary>批量添加向量。已存在的 id 覆盖。</summary>
    public void AddRange(IReadOnlyList<(string Id, float[] Vector)> items) {
        ArgumentNullException.ThrowIfNull(items);
        while (true) {
            var snapshot = _vectors;
            var updated = snapshot;
            foreach (var (id, vector) in items) {
                updated = updated.SetItem(id, vector);
            }
            if (Interlocked.CompareExchange(ref _vectors, updated, snapshot) == snapshot) return;
        }
    }

    /// <summary>删除指定 id 的向量。</summary>
    public void Remove(string id) {
        ArgumentNullException.ThrowIfNull(id);
        while (true) {
            var snapshot = _vectors;
            if (Interlocked.CompareExchange(ref _vectors, snapshot.Remove(id), snapshot) == snapshot) return;
        }
    }

    /// <summary>
    /// 搜索 top-K 最相似向量（余弦相似度）。
    /// <para>遍历全部向量，SIMD 计算相似度，降序排序取前 K 个。</para>
    /// </summary>
    public IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(query);
        if (topK <= 0) return [];

        var snapshot = _vectors;
        if (snapshot.Count == 0) return [];

        var results = new List<(string Id, float Score)>(snapshot.Count);
        foreach (var kvp in snapshot) {
            ct.ThrowIfCancellationRequested();
            var score = VectorMath.CosineSimilarity(query, kvp.Value);
            results.Add((kvp.Key, score));
        }

        results.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return results.Count <= topK ? results : results.GetRange(0, topK);
    }
}
