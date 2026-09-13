namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 弹窗分类 — 对应 PRD E-01 的弹窗分类策略
/// </summary>
public enum PopupCategory
{
    /// <summary>非弹窗</summary>
    None,

    /// <summary>可自主关闭（通知/提示框）</summary>
    Closeable,

    /// <summary>需用户决策（保存覆盖/确认删除）</summary>
    NeedsDecision,

    /// <summary>可重试（网络超时/临时错误）</summary>
    Retryable,
}

/// <summary>
/// 弹窗信息 — 检测到的非预期弹窗，含句柄/标题/文本/分类
/// </summary>
public sealed record PopupInfo(IntPtr Handle, string Title, string? Text, PopupCategory Category);
