namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed class ReferenceIndex
{
    private readonly ConcurrentDictionary<string, IndexedReference> _references;
    private readonly ConcurrentDictionary<string, List<string>> _keywordIndex;

    public DateTimeOffset CreatedAt { get; }

    public string ProjectRoot { get; }

    public int Count => _references.Count;

    public ReferenceIndex(string projectRoot)
    {
        ProjectRoot = projectRoot;
        CreatedAt = DateTimeOffset.UtcNow;
        _references = new ConcurrentDictionary<string, IndexedReference>(StringComparer.OrdinalIgnoreCase);
        _keywordIndex = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public void AddReference(IndexedReference reference)
    {
        _references[reference.Path] = reference;

        foreach (var keyword in reference.Keywords)
        {
            _keywordIndex.AddOrUpdate(
                keyword,
                [reference.Path],
                (_, list) =>
                {
                    list.Add(reference.Path);
                    return list;
                });
        }
    }

    public IndexedReference? FindByPath(string path)
    {
        _references.TryGetValue(path, out var reference);
        return reference;
    }

    public IReadOnlyList<string> FindByKeyword(string keyword)
    {
        if (_keywordIndex.TryGetValue(keyword, out var paths))
        {
            return paths;
        }

        var matches = new List<string>();
        foreach (var kvp in _keywordIndex)
        {
            if (kvp.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                keyword.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                matches.AddRange(kvp.Value);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyCollection<IndexedReference> GetAllReferences()
        => _references.Values.ToList();

    public bool ContainsPath(string path)
        => _references.ContainsKey(path);

    public void Clear()
    {
        _references.Clear();
        _keywordIndex.Clear();
    }
}
