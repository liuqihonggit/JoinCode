namespace NativeTui.Core;

/// <summary>JCC 功能开关门面 — 统一消费环境变量，禁止散落 ad-hoc GetEnvironmentVariable 调用。
/// 助记符词典见 AGENTS.md「开关优先 + 助记符命名」。三态语义：未设置=默认，1/true/yes=启用，0/false/no=恢复默认。
/// 自动适配：OSC 8 支持（Windows Terminal）→ 默认禁用鼠标追踪（原生选区 + OSC 8 hover/点击）；
/// OSC 8 不支持（普通控制台）→ 默认启用鼠标追踪（hover + 点击进第二屏，选区需 Shift）。
/// 环境变量覆盖：JCC_FORCE_MOUSE_TRACKING=1 强制启用；JCC_NO_MOUSE_TRACKING=1 强制禁用。</summary>
public static class JccFeatureFlags
{
    /// <summary>鼠标追踪模式（DEC 1000/1002/1003/1006）启用。
    /// 自动适配：OSC 8 支持 → 默认禁用（原生选区 + OSC 8）；不支持 → 默认启用（hover + 点击进第二屏）。
    /// JCC_FORCE_MOUSE_TRACKING=1 强制启用；JCC_NO_MOUSE_TRACKING=1 强制禁用。</summary>
    public static bool MouseTrackingEnabled
    {
        get
        {
            if (IsFlagSet("JCC_FORCE_MOUSE_TRACKING")) return true;
            if (IsFlagSet("JCC_NO_MOUSE_TRACKING")) return false;
            return !HyperlinkEmitter.IsHyperlinkSupported();
        }
    }

    /// <summary>hover 高亮启用。
    /// OSC 8 模式：终端原生下划线（常量链接标识）；鼠标追踪模式：Move 事件驱动 IsHovered。
    /// JCC_NO_HOVER 设置时禁用。默认启用。</summary>
    public static bool HoverHighlightEnabled => !IsFlagSet("JCC_NO_HOVER");

    /// <summary>点击展开 PromptContextItem 第二屏启用（鼠标追踪模式 — Press 事件分派）。
    /// OSC 8 模式下点击走终端原生打开 .md 文件，此开关不影响；ctrl+o 键盘入口始终可用。
    /// JCC_NO_CLICK_EXPAND 设置时禁用。默认启用。</summary>
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
