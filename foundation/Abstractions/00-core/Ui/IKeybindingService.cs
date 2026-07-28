namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 快捷键绑定服务接口 - 提供按键绑定的注册、查询和管理功能
/// </summary>
public interface IKeybindingService
{
    /// <summary>
    /// 注册快捷键绑定
    /// </summary>
    void Register(string actionId, ConsoleKey key, bool ctrl = false, bool alt = false, bool shift = false, string? description = null);

    /// <summary>
    /// 取消注册快捷键绑定
    /// </summary>
    void Unregister(string actionId);

    /// <summary>
    /// 根据按键组合查找绑定
    /// </summary>
    KeyBinding? FindBinding(ConsoleKey key, bool ctrl, bool alt, bool shift);

    /// <summary>
    /// 获取所有已注册的快捷键绑定
    /// </summary>
    IReadOnlyList<KeyBinding> GetAllBindings();

    /// <summary>
    /// 根据按键组合获取动作标识符
    /// </summary>
    string? GetActionId(ConsoleKey key, bool ctrl, bool alt, bool shift);

    /// <summary>
    /// 快捷键绑定变更事件
    /// </summary>
    event EventHandler<KeyBindingEventArgs>? KeyBindingChanged;
}

/// <summary>
/// 快捷键绑定信息
/// </summary>
public sealed class KeyBinding
{
    /// <summary>动作标识符</summary>
    public string ActionId { get; init; } = string.Empty;

    /// <summary>控制台按键</summary>
    public ConsoleKey Key { get; init; }

    /// <summary>是否按下 Ctrl</summary>
    public bool Ctrl { get; init; }

    /// <summary>是否按下 Alt</summary>
    public bool Alt { get; init; }

    /// <summary>是否按下 Shift</summary>
    public bool Shift { get; init; }

    /// <summary>绑定描述</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 生成可读的快捷键显示字符串，例如 "Ctrl+C"
    /// </summary>
    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>
/// 快捷键绑定变更事件参数
/// </summary>
public sealed class KeyBindingEventArgs : EventArgs
{
    /// <summary>变更的动作标识符</summary>
    public string ActionId { get; init; } = string.Empty;

    /// <summary>变更前的绑定</summary>
    public KeyBinding? OldBinding { get; init; }

    /// <summary>变更后的绑定</summary>
    public KeyBinding? NewBinding { get; init; }
}
