namespace NativeTui.Core.Theme;

/// <summary>
/// 主题 — 语义色键全量对齐基准 utils/theme.ts 的 darkTheme/lightTheme（75 键）。
/// 属性名 PascalCase 化（claude → Claude）；FOR_SUBAGENTS_ONLY/FOR_SYSTEM_SPINNER 后缀保留语义。
/// M3 落地 Dark/Light 两套（REPL 主界面所需），色盲/ANSI 四套后续补充。
/// </summary>
public sealed record Theme
{
    // === 品牌与模式色 ===
    /// <summary>Claude 橙（品牌主色，dark/light 同值 rgb(215,119,87)）。</summary>
    public Color Claude { get; init; }

    /// <summary>Claude 橙 shimmer 变体（流光动画亮色端）。</summary>
    public Color ClaudeShimmer { get; init; }

    /// <summary>系统消息 spinner 蓝。</summary>
    public Color ClaudeBlueForSystemSpinner { get; init; }

    /// <summary>系统 spinner 蓝 shimmer。</summary>
    public Color ClaudeBlueShimmerForSystemSpinner { get; init; }

    /// <summary>权限模式色（蓝紫）。</summary>
    public Color Permission { get; init; }

    /// <summary>权限色 shimmer。</summary>
    public Color PermissionShimmer { get; init; }

    /// <summary>计划模式色（sage 绿）。</summary>
    public Color PlanMode { get; init; }

    /// <summary>自动接受模式色（电紫）。</summary>
    public Color AutoAccept { get; init; }

    /// <summary>bash 边框色（亮粉）。</summary>
    public Color BashBorder { get; init; }

    /// <summary>IDE 集成色（哑蓝）。</summary>
    public Color Ide { get; init; }

    // === 输入框 ===
    /// <summary>输入框边框灰。</summary>
    public Color PromptBorder { get; init; }

    /// <summary>输入框边框 shimmer。</summary>
    public Color PromptBorderShimmer { get; init; }

    // === 文本 ===
    /// <summary>正文色。</summary>
    public Color Text { get; init; }

    /// <summary>反色文本。</summary>
    public Color InverseText { get; init; }

    /// <summary>非活动灰。</summary>
    public Color Inactive { get; init; }

    /// <summary>非活动灰 shimmer。</summary>
    public Color InactiveShimmer { get; init; }

    /// <summary>微弱灰（深）。</summary>
    public Color Subtle { get; init; }

    /// <summary>建议提示色（蓝紫）。</summary>
    public Color Suggestion { get; init; }

    /// <summary>记忆提醒色。</summary>
    public Color Remember { get; init; }

    /// <summary>背景强调色（亮青，基准原样）。</summary>
    public Color Background { get; init; }

    /// <summary>成功绿。</summary>
    public Color Success { get; init; }

    /// <summary>错误红。</summary>
    public Color Error { get; init; }

    /// <summary>警告琥珀。</summary>
    public Color Warning { get; init; }

    /// <summary>警告 shimmer。</summary>
    public Color WarningShimmer { get; init; }

    /// <summary>合并色（电紫，同 autoAccept）。</summary>
    public Color Merged { get; init; }

    // === diff 三级色 ===
    /// <summary>diff 新增行。</summary>
    public Color DiffAdded { get; init; }

    /// <summary>diff 删除行。</summary>
    public Color DiffRemoved { get; init; }

    /// <summary>diff 新增行（暗淡上下文）。</summary>
    public Color DiffAddedDimmed { get; init; }

    /// <summary>diff 删除行（暗淡上下文）。</summary>
    public Color DiffRemovedDimmed { get; init; }

    /// <summary>diff 新增词级。</summary>
    public Color DiffAddedWord { get; init; }

    /// <summary>diff 删除词级。</summary>
    public Color DiffRemovedWord { get; init; }

    // === 子代理八色板 ===
    /// <summary>子代理红。</summary>
    public Color RedForSubagentsOnly { get; init; }

    /// <summary>子代理蓝。</summary>
    public Color BlueForSubagentsOnly { get; init; }

    /// <summary>子代理绿。</summary>
    public Color GreenForSubagentsOnly { get; init; }

    /// <summary>子代理黄。</summary>
    public Color YellowForSubagentsOnly { get; init; }

    /// <summary>子代理紫。</summary>
    public Color PurpleForSubagentsOnly { get; init; }

    /// <summary>子代理橙。</summary>
    public Color OrangeForSubagentsOnly { get; init; }

    /// <summary>子代理粉。</summary>
    public Color PinkForSubagentsOnly { get; init; }

    /// <summary>子代理青。</summary>
    public Color CyanForSubagentsOnly { get; init; }

    // === 消息背景 ===
    /// <summary>用户消息灰底。</summary>
    public Color UserMessageBackground { get; init; }

    /// <summary>用户消息悬停底。</summary>
    public Color UserMessageBackgroundHover { get; init; }

    /// <summary>消息操作区底（冷灰）。</summary>
    public Color MessageActionsBackground { get; init; }

    /// <summary>文本选择底色。</summary>
    public Color SelectionBg { get; init; }

    /// <summary>bash 消息底色。</summary>
    public Color BashMessageBackgroundColor { get; init; }

    /// <summary>记忆消息底色。</summary>
    public Color MemoryBackgroundColor { get; init; }

    // === 吉祥物/杂项 ===
    /// <summary>clawd 吉祥物身体色。</summary>
    public Color ClawdBody { get; init; }

    /// <summary>clawd 吉祥物背景。</summary>
    public Color ClawdBackground { get; init; }

    /// <summary>专业蓝（grove）。</summary>
    public Color ProfessionalBlue { get; init; }

    /// <summary>Chrome 黄。</summary>
    public Color ChromeYellow { get; init; }

    /// <summary>速率限制条填充。</summary>
    public Color RateLimitFill { get; init; }

    /// <summary>速率限制条空槽。</summary>
    public Color RateLimitEmpty { get; init; }

    /// <summary>fast 模式电橙。</summary>
    public Color FastMode { get; init; }

    /// <summary>fast 模式 shimmer。</summary>
    public Color FastModeShimmer { get; init; }

    /// <summary>简报标签：你。</summary>
    public Color BriefLabelYou { get; init; }

    /// <summary>简报标签：Claude。</summary>
    public Color BriefLabelClaude { get; init; }

    // === 彩虹 7 色 + shimmer ===
    /// <summary>彩虹红。</summary>
    public Color RainbowRed { get; init; }

    /// <summary>彩虹橙。</summary>
    public Color RainbowOrange { get; init; }

    /// <summary>彩虹黄。</summary>
    public Color RainbowYellow { get; init; }

    /// <summary>彩虹绿。</summary>
    public Color RainbowGreen { get; init; }

    /// <summary>彩虹蓝。</summary>
    public Color RainbowBlue { get; init; }

    /// <summary>彩虹靛。</summary>
    public Color RainbowIndigo { get; init; }

    /// <summary>彩虹紫。</summary>
    public Color RainbowViolet { get; init; }

    /// <summary>彩虹红 shimmer。</summary>
    public Color RainbowRedShimmer { get; init; }

    /// <summary>彩虹橙 shimmer。</summary>
    public Color RainbowOrangeShimmer { get; init; }

    /// <summary>彩虹黄 shimmer。</summary>
    public Color RainbowYellowShimmer { get; init; }

    /// <summary>彩虹绿 shimmer。</summary>
    public Color RainbowGreenShimmer { get; init; }

    /// <summary>彩虹蓝 shimmer。</summary>
    public Color RainbowBlueShimmer { get; init; }

    /// <summary>彩虹靛 shimmer。</summary>
    public Color RainbowIndigoShimmer { get; init; }

    /// <summary>彩虹紫 shimmer。</summary>
    public Color RainbowVioletShimmer { get; init; }

    /// <summary>暗色主题（默认）— 全量对齐基准 theme.ts darkTheme。</summary>
    public static Theme Dark { get; } = new()
    {
        Claude = Color.FromRgb(215, 119, 87),
        ClaudeShimmer = Color.FromRgb(235, 159, 127),
        ClaudeBlueForSystemSpinner = Color.FromRgb(147, 165, 255),
        ClaudeBlueShimmerForSystemSpinner = Color.FromRgb(177, 195, 255),
        Permission = Color.FromRgb(177, 185, 249),
        PermissionShimmer = Color.FromRgb(207, 215, 255),
        PlanMode = Color.FromRgb(72, 150, 140),
        AutoAccept = Color.FromRgb(175, 135, 255),
        BashBorder = Color.FromRgb(253, 93, 177),
        Ide = Color.FromRgb(71, 130, 200),
        PromptBorder = Color.FromRgb(136, 136, 136),
        PromptBorderShimmer = Color.FromRgb(166, 166, 166),
        Text = Color.FromRgb(255, 255, 255),
        InverseText = Color.FromRgb(0, 0, 0),
        Inactive = Color.FromRgb(153, 153, 153),
        InactiveShimmer = Color.FromRgb(193, 193, 193),
        Subtle = Color.FromRgb(80, 80, 80),
        Suggestion = Color.FromRgb(177, 185, 249),
        Remember = Color.FromRgb(177, 185, 249),
        Background = Color.FromRgb(0, 204, 204),
        Success = Color.FromRgb(78, 186, 101),
        Error = Color.FromRgb(255, 107, 128),
        Warning = Color.FromRgb(255, 193, 7),
        Merged = Color.FromRgb(175, 135, 255),
        WarningShimmer = Color.FromRgb(255, 223, 57),
        DiffAdded = Color.FromRgb(34, 92, 43),
        DiffRemoved = Color.FromRgb(122, 41, 54),
        DiffAddedDimmed = Color.FromRgb(71, 88, 74),
        DiffRemovedDimmed = Color.FromRgb(105, 72, 77),
        DiffAddedWord = Color.FromRgb(56, 166, 96),
        DiffRemovedWord = Color.FromRgb(179, 89, 107),
        RedForSubagentsOnly = Color.FromRgb(220, 38, 38),
        BlueForSubagentsOnly = Color.FromRgb(37, 99, 235),
        GreenForSubagentsOnly = Color.FromRgb(22, 163, 74),
        YellowForSubagentsOnly = Color.FromRgb(202, 138, 4),
        PurpleForSubagentsOnly = Color.FromRgb(147, 51, 234),
        OrangeForSubagentsOnly = Color.FromRgb(234, 88, 12),
        PinkForSubagentsOnly = Color.FromRgb(219, 39, 119),
        CyanForSubagentsOnly = Color.FromRgb(8, 145, 178),
        UserMessageBackground = Color.FromRgb(55, 55, 55),
        UserMessageBackgroundHover = Color.FromRgb(70, 70, 70),
        MessageActionsBackground = Color.FromRgb(44, 50, 62),
        SelectionBg = Color.FromRgb(38, 79, 120),
        BashMessageBackgroundColor = Color.FromRgb(65, 60, 65),
        MemoryBackgroundColor = Color.FromRgb(55, 65, 70),
        ClawdBody = Color.FromRgb(215, 119, 87),
        ClawdBackground = Color.FromRgb(0, 0, 0),
        ProfessionalBlue = Color.FromRgb(106, 155, 204),
        ChromeYellow = Color.FromRgb(251, 188, 4),
        RateLimitFill = Color.FromRgb(177, 185, 249),
        RateLimitEmpty = Color.FromRgb(80, 83, 112),
        FastMode = Color.FromRgb(255, 120, 20),
        FastModeShimmer = Color.FromRgb(255, 165, 70),
        BriefLabelYou = Color.FromRgb(122, 180, 232),
        BriefLabelClaude = Color.FromRgb(215, 119, 87),
        RainbowRed = Color.FromRgb(235, 95, 87),
        RainbowOrange = Color.FromRgb(245, 139, 87),
        RainbowYellow = Color.FromRgb(250, 195, 95),
        RainbowGreen = Color.FromRgb(145, 200, 130),
        RainbowBlue = Color.FromRgb(130, 170, 220),
        RainbowIndigo = Color.FromRgb(155, 130, 200),
        RainbowViolet = Color.FromRgb(200, 130, 180),
        RainbowRedShimmer = Color.FromRgb(250, 155, 147),
        RainbowOrangeShimmer = Color.FromRgb(255, 185, 137),
        RainbowYellowShimmer = Color.FromRgb(255, 225, 155),
        RainbowGreenShimmer = Color.FromRgb(185, 230, 180),
        RainbowBlueShimmer = Color.FromRgb(180, 205, 240),
        RainbowIndigoShimmer = Color.FromRgb(195, 180, 230),
        RainbowVioletShimmer = Color.FromRgb(230, 180, 210),
    };

    /// <summary>亮色主题 — 对齐基准 theme.ts lightTheme（REPL 所需键，其余后续补充）。</summary>
    public static Theme Light { get; } = Dark with
    {
        Claude = Color.FromRgb(215, 119, 87),
        ClaudeShimmer = Color.FromRgb(245, 149, 117),
        Permission = Color.FromRgb(88, 101, 242),
        PermissionShimmer = Color.FromRgb(118, 131, 255),
        PlanMode = Color.FromRgb(106, 165, 145),
        PromptBorder = Color.FromRgb(153, 153, 153),
        PromptBorderShimmer = Color.FromRgb(183, 183, 183),
        Text = Color.FromRgb(0, 0, 0),
        InverseText = Color.FromRgb(255, 255, 255),
        Inactive = Color.FromRgb(153, 153, 153),
        InactiveShimmer = Color.FromRgb(193, 193, 193),
        Subtle = Color.FromRgb(188, 188, 188),
        Suggestion = Color.FromRgb(88, 101, 242),
        Remember = Color.FromRgb(88, 101, 242),
        Success = Color.FromRgb(26, 148, 66),
        Error = Color.FromRgb(215, 55, 63),
        Warning = Color.FromRgb(196, 143, 0),
        WarningShimmer = Color.FromRgb(226, 173, 50),
        DiffAdded = Color.FromRgb(200, 236, 200),
        DiffRemoved = Color.FromRgb(255, 220, 220),
        DiffAddedDimmed = Color.FromRgb(235, 245, 235),
        DiffRemovedDimmed = Color.FromRgb(250, 235, 235),
        DiffAddedWord = Color.FromRgb(28, 128, 48),
        DiffRemovedWord = Color.FromRgb(197, 72, 75),
        UserMessageBackground = Color.FromRgb(240, 240, 240),
        UserMessageBackgroundHover = Color.FromRgb(232, 232, 232),
        SelectionBg = Color.FromRgb(180, 213, 255),
        BashMessageBackgroundColor = Color.FromRgb(250, 245, 250),
        MemoryBackgroundColor = Color.FromRgb(230, 245, 250),
        RateLimitFill = Color.FromRgb(51, 102, 255),
        RateLimitEmpty = Color.FromRgb(23, 46, 114),
        FastMode = Color.FromRgb(255, 106, 0),
        FastModeShimmer = Color.FromRgb(255, 150, 50),
        BriefLabelYou = Color.FromRgb(37, 99, 235),
        BriefLabelClaude = Color.FromRgb(255, 153, 51),
    };
}
