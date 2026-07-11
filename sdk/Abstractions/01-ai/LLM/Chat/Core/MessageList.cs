namespace JoinCode.Abstractions.LLM.Chat;

public sealed class MessageList : IList<ApiMessage>, IReadOnlyList<ApiMessage>
{
    private readonly List<ApiMessage> _messages;

    public MessageList()
    {
        _messages = [];
    }

    public MessageList(IEnumerable<ApiMessage> messages)
    {
        _messages = [.. messages];
    }

    public ApiMessage this[int index]
    {
        get => _messages[index];
        set => _messages[index] = value;
    }

    public int Count => _messages.Count;
    public bool IsReadOnly => false;

    public void Add(ApiMessage item) => _messages.Add(item);
    public void AddRange(IEnumerable<ApiMessage> items) => _messages.AddRange(items);
    public void Clear() => _messages.Clear();

    /// <summary>
    /// 原子替换全部消息 — 对齐 TS applyToolResultBudget 返回新数组后直接赋值
    /// 避免 Clear()+AddRange() 非原子窗口（并发读者可能看到空列表）
    /// </summary>
    public void ReplaceAll(IReadOnlyList<ApiMessage> newMessages)
    {
        _messages.Clear();
        // 先构建完整列表再一次性 AddRange，缩小非原子窗口
        _messages.EnsureCapacity(newMessages.Count);
        _messages.AddRange(newMessages);
    }
    public bool Contains(ApiMessage item) => _messages.Contains(item);
    public void CopyTo(ApiMessage[] array, int arrayIndex) => _messages.CopyTo(array, arrayIndex);
    public int IndexOf(ApiMessage item) => _messages.IndexOf(item);
    public void Insert(int index, ApiMessage item) => _messages.Insert(index, item);
    public bool Remove(ApiMessage item) => _messages.Remove(item);
    public void RemoveAt(int index) => _messages.RemoveAt(index);

    public void AddSystemMessage(string content) => _messages.Add(new ApiMessage(MessageRole.System, content));
    public void AddUserMessage(string content) => _messages.Add(new ApiMessage(MessageRole.User, content));
    public void AddAssistantMessage(string content) => _messages.Add(new ApiMessage(MessageRole.Assistant, content));
    public void AddToolMessage(string content) => _messages.Add(new ApiMessage(MessageRole.Tool, content));

    public IEnumerator<ApiMessage> GetEnumerator() => _messages.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _messages.GetEnumerator();
}
