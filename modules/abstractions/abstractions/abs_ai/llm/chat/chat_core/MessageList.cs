namespace JoinCode.Abstractions.LLM.Chat;

public sealed class MessageList : IList<ApiMessage>, IReadOnlyList<ApiMessage> {
    private readonly List<ApiMessage> _messages;

    public MessageList() {
        _messages = [];
    }

    /// <summary>构造消息列表。</summary>
    public MessageList(IEnumerable<ApiMessage> messages) {
        _messages = [.. messages];
    }

    /// <summary>
    /// 零拷贝工厂 — 直接接管传入的 List，不复制元素。
    /// 调用方在此调用后不得再使用原 List 引用（所有权转移）。
    /// </summary>
    public static MessageList FromList(List<ApiMessage> messages) {
        return new MessageList(messages, owns: true);
    }

    private MessageList(List<ApiMessage> messages, bool owns) {
        _messages = messages;
    }

    public ApiMessage this[int index] {
        get => _messages[index];
        set => _messages[index] = value;
    }

    /// <summary>获取消息数量。</summary>
    public int Count => _messages.Count;
    /// <summary>获取是否只读。</summary>
    public bool IsReadOnly => false;

    /// <summary>添加消息。</summary>
    public void Add(ApiMessage item) => _messages.Add(item);
    /// <summary>添加消息范围。</summary>
    public void AddRange(IEnumerable<ApiMessage> items) => _messages.AddRange(items);
    /// <summary>清空所有消息。</summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// 原子替换全部消息 — 对齐 TS applyToolResultBudget 返回新数组后直接赋值
    /// 避免 Clear()+AddRange() 非原子窗口（并发读者可能看到空列表）
    /// </summary>
    public void ReplaceAll(IReadOnlyList<ApiMessage> newMessages) {
        _messages.Clear();
        // 先构建完整列表再一次性 AddRange，缩小非原子窗口
        _messages.EnsureCapacity(newMessages.Count);
        _messages.AddRange(newMessages);
    }
    /// <summary>判断是否包含指定消息。</summary>
    public bool Contains(ApiMessage item) => _messages.Contains(item);
    /// <summary>复制消息到数组。</summary>
    public void CopyTo(ApiMessage[] array, int arrayIndex) => _messages.CopyTo(array, arrayIndex);
    /// <summary>获取指定消息的索引。</summary>
    public int IndexOf(ApiMessage item) => _messages.IndexOf(item);
    /// <summary>在指定位置插入消息。</summary>
    public void Insert(int index, ApiMessage item) => _messages.Insert(index, item);
    /// <summary>移除指定消息。</summary>
    public bool Remove(ApiMessage item) => _messages.Remove(item);
    /// <summary>移除指定位置的消息。</summary>
    public void RemoveAt(int index) => _messages.RemoveAt(index);

    /// <summary>添加系统消息。</summary>
    public void AddSystemMessage(string content) => _messages.Add(new ApiMessage(MessageRole.System, content));
    /// <summary>添加用户消息。</summary>
    public void AddUserMessage(string content) => _messages.Add(new ApiMessage(MessageRole.User, content));
    /// <summary>添加助手消息。</summary>
    public void AddAssistantMessage(string content) => _messages.Add(new ApiMessage(MessageRole.Assistant, content));
    /// <summary>添加工具消息。</summary>
    public void AddToolMessage(string content) => _messages.Add(new ApiMessage(MessageRole.Tool, content));

    /// <summary>获取枚举器。</summary>
    public IEnumerator<ApiMessage> GetEnumerator() => _messages.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _messages.GetEnumerator();
}