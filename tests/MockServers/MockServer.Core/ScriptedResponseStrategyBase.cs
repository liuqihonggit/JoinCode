namespace MockServer.Core;

/// <summary>
/// 脚本化响应策略基类 — 按预设脚本序列返回响应，支持工具调用和思考内容
/// 每次请求按顺序消费一个 ScriptedTurn，脚本耗尽时返回默认响应
/// </summary>
public abstract class ScriptedResponseStrategyBase : IResponseStrategy
{
    private readonly List<ScriptedTurn> _turns;
    private int _turnIndex;
    private readonly object _lock = new();
    private ScriptedTurn? _currentTurn;
    private ScriptedTurn? _defaultTurnCache;

    /// <summary>
    /// 脚本耗尽时的默认响应
    /// </summary>
    protected string DefaultResponse { get; }

    /// <summary>
    /// 是否支持流式响应
    /// </summary>
    public virtual bool SupportsStreaming => true;

    protected ScriptedResponseStrategyBase(List<ScriptedTurn>? turns, string defaultResponse)
    {
        _turns = turns ?? [];
        DefaultResponse = string.IsNullOrEmpty(defaultResponse) ? "Mock response (script exhausted)." : defaultResponse;
    }

    /// <summary>
    /// 请求开始时调用 — 消费一个脚本轮次并缓存
    /// </summary>
    public virtual void OnRequestStarted(JsonElement request)
    {
        lock (_lock)
        {
            if (_turnIndex < _turns.Count)
                _currentTurn = _turns[_turnIndex++];
            else
                _currentTurn = new ScriptedTurn { TextResponse = DefaultResponse };
        }
    }

    /// <summary>
    /// 当前轮次（OnRequestStarted 后有效，缓存默认值避免重复创建）
    /// </summary>
    protected ScriptedTurn CurrentTurn
    {
        get
        {
            if (_currentTurn is not null) return _currentTurn;
            return _defaultTurnCache ??= new ScriptedTurn { TextResponse = DefaultResponse };
        }
    }

    /// <summary>
    /// 当前轮次是否包含工具调用
    /// </summary>
    public virtual bool HasToolCalls() => CurrentTurn.ToolCalls is { Count: > 0 };

    /// <summary>
    /// 当前轮次是否有思考内容
    /// </summary>
    public virtual bool HasThinkingContent() => !string.IsNullOrEmpty(CurrentTurn.ThinkingContent);

    /// <summary>
    /// 获取流式响应的内容分片 — 使用当前轮次的文本响应
    /// </summary>
    public virtual string[] GetContentChunks()
    {
        var text = CurrentTurn.TextResponse ?? DefaultResponse;
        return GetContentChunks(text);
    }

    /// <summary>
    /// 将文本按空格分片 — 分片本身携带前导空格，拼接后与原文本完全一致。
    /// 例: "I have read" → ["I", " have", " read"]，string.Concat 后为 "I have read"。
    /// 与 IResponseStrategy.GetContentChunks 默认实现的模式保持一致
    /// （["Hello", "!", " This", " is", ...]）。
    /// </summary>
    protected static string[] GetContentChunks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [" "];

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c == ' ')
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                current.Append(' ');
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks.Count == 0 ? [" "] : chunks.ToArray();
    }

    /// <summary>
    /// 生成工具调用 ID
    /// </summary>
    protected static string GenerateToolCallId(ToolCallConfig config)
        => !string.IsNullOrEmpty(config.ToolCallId) ? config.ToolCallId : $"call_{Guid.NewGuid():N}";

    public abstract string BuildResponse(JsonElement request, CacheStats cacheStats);
    public abstract string BuildStreamChunk(string id, string content, bool isLast);
    public abstract string? BuildStreamPreamble(string id);
    public abstract string BuildToolCallResponse(JsonElement request, CacheStats cacheStats);
    public abstract string BuildStreamToolCallResponse(string id);
    public abstract string BuildStreamThinkingResponse(string id);

    /// <summary>
    /// 根据当前脚本轮次的 HttpStatusCode 字段返回 HTTP 状态码。
    /// 默认 200。非 200 时 KestrelMockServer 会返回错误响应。
    /// </summary>
    public virtual int GetHttpStatusCode(JsonElement request)
    {
        lock (_lock)
        {
            return CurrentTurn.HttpStatusCode ?? 200;
        }
    }

    /// <summary>
    /// 构建流式响应的最终 chunk（包含 usage/cache stats）。
    /// 默认实现回退到 BuildStreamChunk(id, "", true) — 不包含 cache stats。
    /// 派生类应 override 此方法, 返回包含协议特定 cache stats 字段的最终 chunk。
    /// </summary>
    public virtual string BuildStreamFinalChunk(string id, CacheStats cacheStats)
        => BuildStreamChunk(id, "", true);
}
