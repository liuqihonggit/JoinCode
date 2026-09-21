namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed class ReferenceIndex {
    private readonly ConcurrentDictionary<string, IndexedReference> _references;
    private readonly ConcurrentDictionary<string, List<string>> _keywordIndex;

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
        _references = new ConcurrentDictionary<string, IndexedReference>(StringComparer.OrdinalIgnoreCase);
        _keywordIndex = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>添加引用到索引。</summary>
    public void AddReference(IndexedReference reference) {
        _references[reference.Path] = reference;

        foreach (var keyword in reference.Keywords) {
            _keywordIndex.AddOrUpdate(
                keyword,
                [reference.Path],
                (_, list) => {
                    list.Add(reference.Path);
                    return list;
                });
        }
    }

    /// <summary>按路径查找引用。</summary>
    public IndexedReference? FindByPath(string path) {
        _references.TryGetValue(path, out var reference);
        return reference;
    }

    /// <summary>按关键词查找引用路径列表。</summary>
    public IReadOnlyList<string> FindByKeyword(string keyword) {
        if (_keywordIndex.TryGetValue(keyword, out var paths)) {
            return paths;
        }

        var matches = new List<string>();
        foreach (var kvp in _keywordIndex) {
            if (kvp.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                keyword.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) {
                matches.AddRange(kvp.Value);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>获取所有引用。</summary>
    public IEnumerable<IndexedReference> GetAllReferences() => _references.Values;

    /// <summary>判断指定路径是否已索引。</summary>
    public bool ContainsPath(string path)
        => _references.ContainsKey(path);

    /// <summary>清空所有引用。</summary>
    public void Clear() {
        _references.Clear();
        _keywordIndex.Clear();
    }
}