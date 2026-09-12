namespace Core.Prompts;

/// <summary>
/// 提示词配置快照 — 在会话初始化时设置，供各 Section 的 Create() 方法读取
/// 避免 Section 需要构造函数参数，保持 static class 模式
/// </summary>
public sealed class PromptConfigSnapshot
{
    private static readonly AsyncLocal<SystemPromptProviderOptions?> _current = new();

    /// <summary>
    /// 当前会话的配置快照（AsyncLocal 保证线程安全）
    /// </summary>
    public static SystemPromptProviderOptions Current => _current.Value ?? SystemPromptProviderOptions.Default;

    /// <summary>
    /// 设置当前会话的配置快照
    /// </summary>
    public static void SetCurrent(SystemPromptProviderOptions options) => _current.Value = options;

    /// <summary>
    /// 清除当前会话的配置快照
    /// </summary>
    public static void Clear() => _current.Value = null;
}
