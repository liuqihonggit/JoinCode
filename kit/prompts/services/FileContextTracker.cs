namespace Core.Prompts;

/// <summary>
/// 文件上下文追踪器 — 维护当前对话涉及的文件路径与用户消息，供提示词构建消费。
/// </summary>
[Register(typeof(FileContextTracker), ServiceLifetime.Singleton)]
public sealed partial class FileContextTracker : ServiceEntity
{
    private volatile FrozenSet<string> _currentFilePaths = FrozenSet<string>.Empty;
    private volatile string _currentUserMessage = string.Empty;

    /// <summary>
    /// 当前关联的文件路径集合（只读）。
    /// </summary>
    public IReadOnlySet<string> CurrentFilePaths => _currentFilePaths;

    /// <summary>
    /// 当前用户消息文本。
    /// </summary>
    public string CurrentUserMessage => _currentUserMessage;

    /// <summary>
    /// 更新当前关联的文件路径集合。
    /// </summary>
    /// <param name="paths">文件路径数组，null 视为空集。</param>
    public void UpdateFilePaths(string[] paths)
    {
        _currentFilePaths = paths is null ? FrozenSet<string>.Empty : paths.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 更新当前用户消息文本。
    /// </summary>
    /// <param name="message">用户消息文本，null 视为空字符串。</param>
    public void UpdateUserMessage(string message)
    {
        _currentUserMessage = message ?? string.Empty;
    }

    /// <summary>
    /// 清空当前文件路径集合与用户消息。
    /// </summary>
    public void Clear()
    {
        _currentFilePaths = FrozenSet<string>.Empty;
        _currentUserMessage = string.Empty;
    }
}
