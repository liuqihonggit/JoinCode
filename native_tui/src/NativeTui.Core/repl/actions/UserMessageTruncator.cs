namespace NativeTui.Core.Repl;

/// <summary>
/// 用户消息显示截断器（消息操作域）— 对齐基准 UserPromptMessage.tsx 常量 MAX_DISPLAY_CHARS=10_000 /
/// TRUNCATE_HEAD_CHARS=2_500 / TRUNCATE_TAIL_CHARS=2_500：超限 head + 中间截断标记 + tail
///（大文件管道输入防单条消息全量逐帧渲染卡顿；head+tail 因用户实际问题常在尾部）。
/// 纯函数（Span 切片 + 三段 Concat 零中间分配）— 渲染接线在用户消息 item 域（S2，见未决问题）。
/// </summary>
public static class UserMessageTruncator
{
    /// <summary>显示字符上限（基准 MAX_DISPLAY_CHARS）。</summary>
    public const int MaxDisplayChars = 10_000;

    /// <summary>截断保留头部长度（基准 TRUNCATE_HEAD_CHARS）。</summary>
    public const int HeadChars = 2_500;

    /// <summary>截断保留尾部长度（基准 TRUNCATE_TAIL_CHARS）。</summary>
    public const int TailChars = 2_500;

    /// <summary>中间截断标记（head/tail 之间独立两行边界 — 任务指定文案）。</summary>
    public const string TruncationMarker = "\n…截断…\n";

    /// <summary>超限截断（head + 标记 + tail；≤ 上限原样返回不分配）。</summary>
    public static string Truncate(ReadOnlySpan<char> text) =>
        text.Length <= MaxDisplayChars
            ? text.ToString()
            : string.Concat(text[..HeadChars], TruncationMarker.AsSpan(), text[^TailChars..]);
}
