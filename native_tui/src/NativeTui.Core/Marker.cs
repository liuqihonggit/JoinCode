namespace NativeTui.Core;

/// <summary>
/// NativeTui.Core 标记类型 — 用于测试与宿主确认框架程序集身份（零依赖冒烟锚点）。
/// </summary>
public static class Marker
{
    /// <summary>框架显示名。</summary>
    public const string Name = "NativeTui";

    /// <summary>对齐基准源码（Claude Code source map 还原版）的视觉规范版本标识。</summary>
    public const string AlignmentBaseline = "claude-code-rev-main";
}
