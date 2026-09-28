namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed class ReferenceIndex {
    private volatile ImmutableHamT<string, IndexedReference> _references;
    private volatile ImmutableHamT<string, ImmutableList<string>> _keywordIndex;

    /// <summary>获取创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>获取项目根路径。</summary>
    public string ProjectRoot { get; }

    /// <summary>获取引用数量。</summary>
    public int Count => _references.Count;

    /// <summary>构造引用索引。</summary>
    public ReferenceIndex(string projectRoot) {
        ProjectRoot = projectRoot;
        CreatedAt = DateTimeOffset.UtcNow;
        _references = ImmutableHamT<string, IndexedReference>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        _keywordIndex = ImmutableHamT<string, ImmutableList<string>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>添加引用到索引 — 手写 CAS 循环替代 ImmutableInterlocked.Update(volatile 字段触发 CS0420)。</summary>
    public void AddReference(IndexedReference reference) {
        while (true) {
            var current = _references;
            var updated = current.SetItem(reference.Path, reference);
            if (Interlocked.CompareExchange(ref _references, updated, current) == current) break;
        }

        foreach (var keyword in reference.Keywords) {
            while (true) {
                var current = _keywordIndex;
                var updated = current.SetItem(keyword, current.GetValueOrDefault(keyword, ImmutableList<string>.Empty).Add(reference.Path));
                if (Interlocked.CompareExchange(ref _keywordIndex, updated, current) == current) break;
            }
        }
    }

    /// <summary>按路径查找引用。</summary>
    public IndexedReference? FindByPath(string path) {
        _references.TryGetValue(path, out var reference);
        return reference;
    }

    /// <summary>按关键词查找引用路径列表。</summary>
    public IReadOnlyList<string> FindByKeyword(string keyword) {
        var snapshot = _keywordIndex;
        if (snapshot.TryGetValue(keyword, out var paths)) {
            return paths;
        }

        var matches = new List<string>();
        foreach (var kvp in snapshot) {
            if (kvp.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                keyword.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) {
                matches.AddRange(kvp.Value);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>获取所有引用的快照拷贝。</summary>
    public IndexedReference[] GetAllReferences() => _references.Values.ToArray();

    /// <summary>判断指定路径是否已索引。</summary>
    public bool ContainsPath(string path)
        => _references.ContainsKey(path);

    /// <summary>清空所有引用 — 手写 CAS 循环替代 Interlocked.Exchange(volatile 字段触发 CS0420),保留 comparer。</summary>
    public void Clear() {
        while (true) {
            var current = _references;
            if (Interlocked.CompareExchange(ref _references, ImmutableHamT<string, IndexedReference>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), current) == current) break;
        }
        while (true) {
            var current = _keywordIndex;
            if (Interlocked.CompareExchange(ref _keywordIndex, ImmutableHamT<string, ImmutableList<string>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), current) == current) break;
        }
    }
}
