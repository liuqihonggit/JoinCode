namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 只追加消息日志，封装对话消息列表，支持追加、整体压缩与尾部撤回
/// </summary>
public sealed class AppendOnlyLog {
    private readonly List<ApiMessage> _entries = [];

    /// <summary>
    /// 当前日志条目数量
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 按索引获取消息条目
    /// </summary>
    /// <param name="index">条目索引</param>
    /// <returns>指定索引处的消息</returns>
    public ApiMessage this[int index] => _entries[index];

    /// <summary>
    /// 追加一条消息
    /// </summary>
    /// <param name="message">待追加的消息</param>
    public void Append(ApiMessage message) {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Role == default)
            throw new ArgumentException("Message must have a valid role.", nameof(message));
        _entries.Add(message);
    }

    /// <summary>
    /// 批量追加消息
    /// </summary>
    /// <param name="messages">待追加的消息列表</param>
    public void Extend(IReadOnlyList<ApiMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var m in messages) Append(m);
    }

    /// <summary>
    /// 返回消息的只读视图 — 零分配，直接返回内部 List 引用
    /// 安全性: AppendOnlyLog 是追加模式(只 Add 不修改已有条目)，且所有调用方只读不写
    /// CompactInPlace/TrimLastTurn 不会在遍历期间执行(同一 async 流)
    /// </summary>
    public IReadOnlyList<ApiMessage> ToMessages() {
        return _entries;
    }

    /// <summary>
    /// 原地替换全部条目，用于上下文折叠/压缩后重写日志
    /// </summary>
    /// <param name="replacement">替换后的消息列表</param>
    public void CompactInPlace(IReadOnlyList<ApiMessage> replacement) {
        ArgumentNullException.ThrowIfNull(replacement);
        _entries.Clear();
        _entries.AddRange(replacement);
    }

    /// <summary>
    /// 撤回最后一轮对话（SP-3 安全点）。从末尾向前移除，直到遇到 User 消息之前的消息。
    /// 一轮对话 = 最后一条 User 消息 + 其后所有消息（Assistant/Tool）。
    /// </summary>
    /// <returns>移除的消息数量</returns>
    public int TrimLastTurn() {
        if (_entries.Count == 0) return 0;

        var lastUserIndex = -1;
        for (var i = _entries.Count - 1; i >= 0; i--) {
            if (_entries[i].Role == MessageRole.User) {
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
    public int TruncateTo(int index) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index > _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} exceeds count {_entries.Count}");

        var removed = _entries.Count - index;
        if (removed > 0) {
            _entries.RemoveRange(index, removed);
        }
        return removed;
    }
}