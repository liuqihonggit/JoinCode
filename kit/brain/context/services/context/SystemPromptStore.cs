namespace Core.Context;

/// <summary>
/// 系统提示存储 — 管理静态系统提示、动态系统消息和系统消息缓存
/// 从 ChatContextManager 提取,降低大类字段数和复杂度
/// </summary>
internal sealed class SystemPromptStore
{
    private string _staticPrompt = string.Empty;
    private readonly List<string> _dynamicMessages = [];
    private string _previousDynamicHash = string.Empty;
    private List<ApiMessage> _cachedSystemMessages = [];
    private bool _systemMessagesCached;

    /// <summary>静态系统提示</summary>
    public string StaticPrompt
    {
        get => _staticPrompt;
        set => _staticPrompt = value;
    }

    /// <summary>更新静态系统提示，并清空缓存</summary>
    public void Update(string prompt)
    {
        _staticPrompt = prompt;
        InvalidateCache();
    }

    /// <summary>添加动态系统消息，并清空缓存</summary>
    public void AddDynamic(string message)
    {
        _dynamicMessages.Add(message);
        InvalidateCache();
    }

    /// <summary>清除所有动态系统消息</summary>
    public void ClearDynamic()
    {
        _dynamicMessages.Clear();
        InvalidateCache();
    }

    /// <summary>获取动态消息拼接内容（用于缓存破坏检测）</summary>
    public string GetDynamicContent() => string.Join("\n", _dynamicMessages);

    /// <summary>获取动态消息列表引用（用于遍历）</summary>
    public List<string> GetDynamicMessages() => _dynamicMessages;

    /// <summary>获取或创建缓存的系统消息列表 — 动态消息未变时复用缓存</summary>
    public List<ApiMessage> GetOrCreateCachedSystemMessages()
    {
        var dynamicContent = GetDynamicContent();
        var currentDynamicHash = string.IsNullOrEmpty(dynamicContent)
            ? string.Empty
            : ContentHash.Compute(dynamicContent);
        var dynamicChanged = currentDynamicHash != _previousDynamicHash;
        _previousDynamicHash = currentDynamicHash;

        if (!dynamicChanged && _systemMessagesCached)
        {
            return _cachedSystemMessages;
        }

        var systemMessages = new List<ApiMessage>();
        if (!string.IsNullOrWhiteSpace(_staticPrompt))
        {
            systemMessages.Add(new ApiMessage(MessageRole.System, _staticPrompt));
        }

        foreach (var dynamicMsg in _dynamicMessages)
        {
            if (dynamicChanged)
            {
                systemMessages.Add(new ApiMessage(MessageRole.System, dynamicMsg, CacheBreakMarker.Create()));
            }
            else
            {
                systemMessages.Add(new ApiMessage(MessageRole.System, dynamicMsg));
            }
        }

        if (dynamicChanged)
        {
            _cachedSystemMessages = [];
            _systemMessagesCached = false;
        }
        else
        {
            _cachedSystemMessages = systemMessages;
            _systemMessagesCached = true;
        }
        return systemMessages;
    }

    /// <summary>重置缓存（回退到初始状态时调用）</summary>
    public void ResetCache()
    {
        _dynamicMessages.Clear();
        _cachedSystemMessages = [];
        _systemMessagesCached = false;
    }

    private void InvalidateCache()
    {
        _cachedSystemMessages = [];
        _systemMessagesCached = false;
    }
}
