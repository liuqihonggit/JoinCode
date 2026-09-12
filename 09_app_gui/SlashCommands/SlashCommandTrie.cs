namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 斜杠命令前缀树 — 大小写不敏感的前缀匹配数据结构。
/// 命令存储不带前置 /，终端节点挂载命令完整元数据。
/// 支持运行时动态增删，单次查询 O(m)（m 为前缀长度），可流畅支撑 200+ 命令。
/// </summary>
public sealed class SlashCommandTrie
{
    private readonly TrieNode _root = new();
    private readonly Dictionary<string, SlashCommandItem> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前命令数量</summary>
    public int Count => _items.Count;

    /// <summary>默认构造 — 用于动态构建</summary>
    public SlashCommandTrie() { }

    /// <summary>用命令列表构建前缀树（兼容现有 Filter 缓存调用）</summary>
    public SlashCommandTrie(IEnumerable<SlashCommandItem> commands) => InsertRange(commands);

    /// <summary>按前缀匹配命令 — Match 的语义别名，兼容现有调用方</summary>
    public IReadOnlyList<SlashCommandItem> Match(string prefix) => Search(prefix);

    /// <summary>插入命令（重复命令名覆盖旧值）</summary>
    public void Insert(SlashCommandItem item)
    {
        var key = NormalizeKey(item.Name);
        var node = _root;
        foreach (var ch in key)
        {
            if (!node.Children.TryGetValue(ch, out var next))
            {
                next = new TrieNode();
                node.Children[ch] = next;
            }
            node = next;
        }
        node.Item = item;
        _items[key] = item;
    }

    /// <summary>批量插入命令（用于初始化或重建）</summary>
    public void InsertRange(IEnumerable<SlashCommandItem> items)
    {
        foreach (var item in items)
            Insert(item);
    }

    /// <summary>删除命令，返回是否删除成功</summary>
    public bool Remove(string name)
    {
        var key = NormalizeKey(name);
        if (!_items.Remove(key))
            return false;
        RemoveNode(_root, key.AsSpan(), 0);
        return true;
    }

    /// <summary>清空所有命令</summary>
    public void Clear()
    {
        _root.Children.Clear();
        _root.Item = null;
        _items.Clear();
    }

    /// <summary>
    /// 按前缀查询命令。空前缀或仅 / 返回全部命令，无匹配返回空数组。
    /// 大小写不敏感匹配，返回的命令保留原始大小写。
    /// </summary>
    public IReadOnlyList<SlashCommandItem> Search(string prefix)
    {
        var key = NormalizeKey(prefix);
        if (key.Length == 0)
            return _items.Values.ToList();

        var node = _root;
        foreach (var ch in key)
        {
            if (!node.Children.TryGetValue(ch, out node))
                return Array.Empty<SlashCommandItem>();
        }
        var results = new List<SlashCommandItem>();
        Collect(node, results);
        return results;
    }

    private static void Collect(TrieNode node, List<SlashCommandItem> results)
    {
        if (node.Item is not null)
            results.Add(node.Item);
        foreach (var child in node.Children.Values)
            Collect(child, results);
    }

    private static bool RemoveNode(TrieNode node, ReadOnlySpan<char> key, int depth)
    {
        if (depth == key.Length)
        {
            node.Item = null;
            return node.Children.Count == 0;
        }
        var ch = key[depth];
        if (!node.Children.TryGetValue(ch, out var child))
            return false;
        var shouldRemoveChild = RemoveNode(child, key, depth + 1);
        if (shouldRemoveChild)
            node.Children.Remove(ch);
        return node.Children.Count == 0 && node.Item is null;
    }

    /// <summary>规范化 key：去掉前导 /，转小写（大小写不敏感匹配）</summary>
    private static string NormalizeKey(string name)
    {
        var span = name.AsSpan();
        if (span.Length > 0 && span[0] == '/')
            span = span[1..];
        return span.ToString().ToLowerInvariant();
    }

    private sealed class TrieNode
    {
        public readonly Dictionary<char, TrieNode> Children = new();
        public SlashCommandItem? Item;
    }
}
