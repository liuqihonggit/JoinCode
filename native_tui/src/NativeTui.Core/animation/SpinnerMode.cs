namespace NativeTui.Core.Animation;

/// <summary>
/// Spinner 模式 — 对齐基准 components/Spinner/types.ts 的 SpinnerMode（string 联合）。
/// 影响动画参数：requesting 用 50ms 快 shimmer 右移；tool-use 用整体 flash 正弦；
/// 其余模式 200ms 慢 shimmer 左移。
/// </summary>
public enum SpinnerMode
{
    /// <summary>思考中（首 token 之前）。</summary>
    Thinking,

    /// <summary>请求发出（shimmer 快速右移）。</summary>
    Requesting,

    /// <summary>响应流式输出中。</summary>
    Responding,

    /// <summary>工具执行中（整体 flash）。</summary>
    ToolUse,

    /// <summary>工具参数接收中。</summary>
    ToolInput,
}
