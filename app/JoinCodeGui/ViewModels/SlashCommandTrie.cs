namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 斜杠命令前缀树 — 按字符构建 trie，实现前缀匹配。
/// 输入 "/a" "/ap" 即可识别出 "/apple"，优于线性 StartsWith 扫描。
/// </summary>
public sealed class SlashCommandTrie
{
    private sealed class Node
    {
        public SortedDictionary<char, Node> Children { get; } = new();
        public SlashCommandItem? Item { get; set; }
    }

    private readonly Node _root = new();
    private readonly List<SlashCommandItem> _all = [];

    /// <summary>
    /// 用命令列表构建前缀树
    /// </summary>
    public SlashCommandTrie(IEnumerable<SlashCommandItem> commands)
    {
        foreach (var command in commands)
        {
            _all.Add(command);
            Insert(command);
        }
    }

    /// <summary>
    /// 插入单个命令到前缀树
    /// </summary>
    private void Insert(SlashCommandItem command)
    {
        var node = _root;
        foreach (var ch in command.Name)
        {
            if (!node.Children.TryGetValue(ch, out var next))
            {
                next = new Node();
                node.Children[ch] = next;
            }
            node = next;
        }
        node.Item = command;
    }

    /// <summary>
    /// 按前缀匹配命令（大小写不敏感）；空前缀返回全部命令
    /// </summary>
    public IReadOnlyList<SlashCommandItem> Match(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return _all;

        var node = _root;
        foreach (var ch in prefix)
        {
            var lowered = char.ToLowerInvariant(ch);
            if (!node.Children.TryGetValue(lowered, out var next))
            {
                var upper = char.ToUpperInvariant(ch);
                if (upper != lowered && node.Children.TryGetValue(upper, out next))
                {
                    node = next;
                    continue;
                }
                return [];
            }
            node = next;
        }

        var result = new List<SlashCommandItem>();
        Collect(node, result);
        return result;
    }

    /// <summary>收集节点子树下的全部命令</summary>
    private static void Collect(Node node, List<SlashCommandItem> result)
    {
        if (node.Item is not null)
            result.Add(node.Item);

        foreach (var child in node.Children.Values)
            Collect(child, result);
    }
}
