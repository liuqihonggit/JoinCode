namespace NativeTui.Core.Repl;

/// <summary>
/// footer 渲染状态 — 聚合基准 PromptInputFooter Props 的可渲染子集：
/// 左侧模式指示（PromptInputFooterLeftSide）+ agents 导航提示（v2.1.303 实测 `← for agents`，
/// 本地基准快照无此字面量，数据源为用户终端第一手文本）
/// + 右侧登录错误（Notifications apiKeyStatus missing，error 色）
/// + 右侧通知（如 effort `◐ medium · /effort`，基准 12s 超时通知，dim）。
/// </summary>
public sealed record FooterState
{
    /// <summary>权限模式（左侧模式指示）。</summary>
    public PermissionMode Mode { get; init; }

    /// <summary>是否忙（输入 ❯ dim + 左侧追加 dim esc to interrupt）。</summary>
    public bool Busy { get; init; }

    /// <summary>agents 导航提示（非 busy 时以 ` · ` 追加在左侧模式串后，dim）。</summary>
    public string? AgentsHint { get; init; }

    /// <summary>右侧登录错误（右对齐 error 色，如 "Not logged in · Run /login"）。</summary>
    public string? LoginError { get; init; }

    /// <summary>右侧通知（行 2 右对齐 dim，如 "◐ medium · /effort"；非空时 footer 占 2 行）。</summary>
    public string? Notification { get; init; }
}
