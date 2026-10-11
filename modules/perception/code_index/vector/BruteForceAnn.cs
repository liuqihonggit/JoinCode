namespace JoinCode.CodeIndex.Vector;

/// <summary>
/// 暴力搜索 ANN 实现 — 遍历全部向量计算余弦相似度，取 top-K。
/// <para>起步方案：零依赖、AOT 友好、简单可靠。</para>
/// <para>适用 &lt;10K 块的中小代码库（实测 0.969ms/10K×256维）。</para>
/// <para>线程安全：读操作（Search）与写操作（Add/Remove）互斥。</para>
/// </summary>
public sealed class BruteForceAnn : IAnnSearch {

    private readonly Dictionary<string, float[]> _vectors = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>当前向量数量。</summary>
    public int Count {
        get {
            _lock.EnterReadLock();
            try {
                return _vectors.Count;
            } finally {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>添加单个向量。若 id 已存在则覆盖。</summary>
    public void Add(string id, float[] vector) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(vector);
        _lock.EnterWriteLock();
        try {
            _vectors[id] = vector;
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>批量添加向量。已存在的 id 覆盖。</summary>
    public void AddRange(IReadOnlyList<(string Id, float[] Vector)> items) {
        ArgumentNullException.ThrowIfNull(items);
        _lock.EnterWriteLock();
        try {
            foreach (var (id, vector) in items) {
                _vectors[id] = vector;
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>删除指定 id 的向量。</summary>
    public void Remove(string id) {
        ArgumentNullException.ThrowIfNull(id);
        _lock.EnterWriteLock();
        try {
            _vectors.Remove(id);
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 搜索 top-K 最相似向量（余弦相似度）。
    /// <para>遍历全部向量，SIMD 计算相似度，降序排序取前 K 个。</para>
    /// </summary>
    public IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(query);
        if (topK <= 0) return [];

        List<(string Id, float[] Vector)> snapshot;
        _lock.EnterReadLock();
        try {
            if (_vectors.Count == 0) return [];
            snapshot = [.. _vectors.Select(kvp => (kvp.Key, kvp.Value))];
        } finally {
            _lock.ExitReadLock();
        }

        var results = new List<(string Id, float Score)>(snapshot.Count);
        foreach (var (id, vector) in snapshot) {
            ct.ThrowIfCancellationRequested();
            var score = VectorMath.CosineSimilarity(query, vector);
            results.Add((id, score));
        }

        results.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return results.Count <= topK ? results : results.GetRange(0, topK);
    }
}
