namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed class ReferenceIndex {
    private ImmutableDictionary<string, IndexedReference> _references;
    private ImmutableDictionary<string, ImmutableList<string>> _keywordIndex;

    /// <summary>获取创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>获取项目根路径。</summary>
    public string ProjectRoot { get; }

    /// <summary>获取引用数量。</summary>
    public int Count => Volatile.Read(ref _references).Count;

    /// <summary>构造引用索引。</summary>
    public ReferenceIndex(string projectRoot) {
        ProjectRoot = projectRoot;
        CreatedAt = DateTimeOffset.UtcNow;
        _references = ImmutableDictionary<string, IndexedReference>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        _keywordIndex = ImmutableDictionary<string, ImmutableList<string>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>添加引用到索引。</summary>
    public void AddReference(IndexedReference reference) {
        ImmutableInterlocked.Update(ref _references, d => d.SetItem(reference.Path, reference));

        foreach (var keyword in reference.Keywords) {
            ImmutableInterlocked.Update(ref _keywordIndex,
                d => d.SetItem(keyword, d.GetValueOrDefault(keyword, ImmutableList<string>.Empty).Add(reference.Path)));
        }
    }

    /// <summary>按路径查找引用。</summary>
    public IndexedReference? FindByPath(string path) {
        Volatile.Read(ref _references).TryGetValue(path, out var reference);
        return reference;
    }

    /// <summary>按关键词查找引用路径列表。</summary>
    public IReadOnlyList<string> FindByKeyword(string keyword) {
        var snapshot = Volatile.Read(ref _keywordIndex);
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
    public IndexedReference[] GetAllReferences() => Volatile.Read(ref _references).Values.ToArray();

    /// <summary>判断指定路径是否已索引。</summary>
    public bool ContainsPath(string path)
        => Volatile.Read(ref _references).ContainsKey(path);

    /// <summary>清空所有引用。</summary>
    public void Clear() {
        Interlocked.Exchange(ref _references, ImmutableDictionary<string, IndexedReference>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
        Interlocked.Exchange(ref _keywordIndex, ImmutableDictionary<string, ImmutableList<string>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
    }
}