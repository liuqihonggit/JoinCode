namespace NativeTui.Core.Figures;

/// <summary>
/// 统一符号常量表 — 全量对齐基准 constants/figures.ts + figures 库（pointer）+ MessageResponse 的工具结果缩进。
/// darwin/其他平台差异保留（BLACK_CIRCLE 垂直对齐差异是基准刻意行为）。
/// </summary>
public static class Figures
{
    /// <summary>消息头圆点 — darwin 用 ⏺（垂直对齐更好），其他平台 ●（兼容性）。</summary>
    public const string BlackCircleDarwin = "⏺";

    /// <summary>消息头圆点（非 darwin 回退）。</summary>
    public const string BlackCircle = "●";

    /// <summary>当前平台消息头圆点（Windows/Linux 用 ●）。</summary>
    public const string MessageBullet = BlackCircle;

    /// <summary>分隔符 ∙。</summary>
    public const string BulletOperator = "∙";

    /// <summary>spinner 相关 ✻。</summary>
    public const string TeardropAsterisk = "✻";

    /// <summary>输入提示符 ❯（基准 figures 库 pointer）。</summary>
    public const string Pointer = "❯";

    /// <summary>工具结果缩进前缀（基准 MessageResponse.tsx：2 空格 + ⎿ + 2 空格，dim 色）。</summary>
    public const string ToolResultIndent = "  ⎿  ";

    /// <summary>上箭头 ↑（opus 1m 合并通知）。</summary>
    public const string UpArrow = "↑";

    /// <summary>下箭头 ↓（滚动提示/token 计数）。</summary>
    public const string DownArrow = "↓";

    /// <summary>闪电 ↯（fast 模式指示）。</summary>
    public const string LightningBolt = "↯";

    /// <summary>推理力度：低 ○。</summary>
    public const string EffortLow = "○";

    /// <summary>推理力度：中 ◐。</summary>
    public const string EffortMedium = "◐";

    /// <summary>推理力度：高 ●。</summary>
    public const string EffortHigh = "●";

    /// <summary>推理力度：最大 ◉。</summary>
    public const string EffortMax = "◉";

    /// <summary>播放 ▶。</summary>
    public const string PlayIcon = "▶";

    /// <summary>暂停 ⏸。</summary>
    public const string PauseIcon = "⏸";

    /// <summary>MCP 资源更新指示 ↻。</summary>
    public const string RefreshArrow = "↻";

    /// <summary>入站频道消息指示 ←。</summary>
    public const string ChannelArrow = "←";

    /// <summary>跨会话注入消息指示 →。</summary>
    public const string InjectedArrow = "→";

    /// <summary>fork 指示 ⑂。</summary>
    public const string ForkGlyph = "⑂";

    /// <summary>review 运行中 ◇。</summary>
    public const string DiamondOpen = "◇";

    /// <summary>review 完成/失败 ◆。</summary>
    public const string DiamondFilled = "◆";

    /// <summary>离开摘要标记 ※。</summary>
    public const string ReferenceMark = "※";

    /// <summary>issue 旗标 ⚑。</summary>
    public const string FlagIcon = "⚑";

    /// <summary>Markdown 引用条 ▎（左四分之一块）。</summary>
    public const string BlockquoteBar = "▎";

    /// <summary>粗分隔线 ━。</summary>
    public const string HeavyHorizontal = "━";

    /// <summary>bridge 就绪指示 ·✔︎·。</summary>
    public const string BridgeReadyIndicator = "·✔︎·";

    /// <summary>bridge 失败指示 ×。</summary>
    public const string BridgeFailedIndicator = "×";

    /// <summary>bridge 动画帧（·|· → ·/· → ·—· → ·\·）。</summary>
    public static readonly string[] BridgeSpinnerFrames = ["·|·", "·/·", "·—·", "·\\·"];
}
