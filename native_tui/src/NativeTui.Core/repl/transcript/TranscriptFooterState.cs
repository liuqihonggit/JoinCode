namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 专用 footer 数据 — 对齐基准 TranscriptModeFooter（REPL.tsx 321-362）：
/// 左侧 dim 标题 + 键位提示；查询激活时渲染层追加 n/N 导航提示与 cur/count 徽章
///（右对齐 `N/M`）；搜索栏打开时渲染层替换为搜索栏（查询 + 徽章）。纯数据类，
/// 渲染由消费方（FooterBar / ReplView）执行，本类不做任何渲染。
/// </summary>
public sealed record TranscriptFooterState
{
    /// <summary>标题（左侧 dim — 基准 "Showing detailed transcript" 的本地化文案）。</summary>
    public const string TitleText = "详细转录";

    /// <summary>键位提示（标题后以 · 连接 — 覆盖切换/滚动/搜索/退出四类核心键）。</summary>
    public const string HintText = "ctrl+o 切换 · ↑↓ 滚动 · / 搜索 · q 退出";

    /// <summary>搜索栏查询（null = 搜索栏未打开 — footer 显示标题+提示；非 null = 搜索栏打开，渲染层切换为搜索栏）。</summary>
    public string? SearchQuery { get; init; }

    /// <summary>搜索是否激活（栏打开或查询非空 — 渲染层据此追加 n/N 导航提示与徽章）。</summary>
    public bool SearchActive { get; init; }

    /// <summary>当前匹配序号（1-based；无匹配 0 — 徽章 `N/M` 的 N）。</summary>
    public int SearchCurrent { get; init; }

    /// <summary>匹配总数（徽章 `N/M` 的 M）。</summary>
    public int SearchCount { get; init; }

    /// <summary>标题+提示合成整行文案（搜索栏未打开时的 footer 主行）。</summary>
    public string FullText => $"{TitleText} · {HintText}";
}
