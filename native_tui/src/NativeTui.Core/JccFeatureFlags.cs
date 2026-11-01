namespace NativeTui.Core;

/// <summary>JCC 功能开关门面 — 统一消费环境变量，禁止散落 ad-hoc GetEnvironmentVariable 调用。
/// 助记符词典见 AGENTS.md「开关优先 + 助记符命名」。三态语义：未设置=默认，1/true/yes=禁用，0/false/no=恢复默认。
/// NO_ 前缀开关：设置=禁用对应行为，未设置=启用（向后兼容默认行为）。</summary>
public static class JccFeatureFlags
{
    /// <summary>鼠标追踪模式（DEC 1000/1002/1003/1006）启用。
    /// JCC_NO_MOUSE_TRACKING 设置时禁用，恢复终端原生选区（鼠标任意选择 + 右键复制无需 Shift）。默认启用。</summary>
    public static bool MouseTrackingEnabled => !IsFlagSet("JCC_NO_MOUSE_TRACKING");

    /// <summary>hover 高亮（Move 事件驱动下划线）启用。
    /// JCC_NO_HOVER 设置时禁用。默认启用。仅 MouseTrackingEnabled=true 时生效。</summary>
    public static bool HoverHighlightEnabled => !IsFlagSet("JCC_NO_HOVER");

    /// <summary>点击展开 PromptContextItem 第二屏启用。
    /// JCC_NO_CLICK_EXPAND 设置时禁用（仅保留 ctrl+o 键盘入口）。默认启用。</summary>
    public static bool ClickExpandEnabled => !IsFlagSet("JCC_NO_CLICK_EXPAND");

    private static bool IsFlagSet(string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(value)) return false;
        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
