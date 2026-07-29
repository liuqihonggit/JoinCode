namespace JoinCode.Abstractions.LLM.Chat;

public sealed class AppendOnlyLog
{
    private readonly List<ApiMessage> _entries = [];

    public int Count => _entries.Count;

    public ApiMessage this[int index] => _entries[index];

    public void Append(ApiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Role == default)
            throw new ArgumentException("Message must have a valid role.", nameof(message));
        _entries.Add(message);
    }

    public void Extend(IReadOnlyList<ApiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var m in messages) Append(m);
    }

    public IReadOnlyList<ApiMessage> ToMessages()
    {
        return _entries.Select(e => new ApiMessage(e.Role, e.Content, e.Metadata)).ToList();
    }

    public void CompactInPlace(IReadOnlyList<ApiMessage> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        _entries.Clear();
        _entries.AddRange(replacement);
    }

    /// <summary>
    /// 撤回最后一轮对话（SP-3 安全点）。从末尾向前移除，直到遇到 User 消息之前的消息。
    /// 一轮对话 = 最后一条 User 消息 + 其后所有消息（Assistant/Tool）。
    /// </summary>
    /// <returns>移除的消息数量</returns>
    public int TrimLastTurn()
    {
        if (_entries.Count == 0) return 0;

        var lastUserIndex = -1;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Role == MessageRole.User)
            {
                lastUserIndex = i;
                break;
            }
        }

        if (lastUserIndex < 0) return 0;

        var removed = _entries.Count - lastUserIndex;
        _entries.RemoveRange(lastUserIndex, removed);
        return removed;
    }

    /// <summary>
    /// 截断到指定索引（SP-5 安全点）。保留 [0, index) 的消息，移除 [index, Count) 的消息。
    /// </summary>
    /// <param name="index">保留的消息数量（截断点）</param>
    /// <returns>移除的消息数量</returns>
    /// <exception cref="ArgumentOutOfRangeException">index 为负数或超过 Count</exception>
    public int TruncateTo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index > _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} exceeds count {_entries.Count}");

        var removed = _entries.Count - index;
        if (removed > 0)
        {
            _entries.RemoveRange(index, removed);
        }
        return removed;
    }
}
