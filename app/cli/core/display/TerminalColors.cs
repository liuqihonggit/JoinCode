namespace JoinCode.Cli;

/// <summary>
/// 终端颜色常量 — CLI 模式下的 ANSI 颜色定义
/// </summary>
public static class TerminalColors {
    #region Brand

    /// <summary>JoinCode 品牌主色</summary>
    public static readonly RgbColor JoinCode = new(215, 119, 87);
    /// <summary>JoinCode 品牌主色微光色（高亮变体）</summary>
    public static readonly RgbColor JoinCodeShimmer = new(235, 159, 127);

    #endregion

    #region Spinner

    /// <summary>加载旋转器蓝色</summary>
    public static readonly RgbColor SpinnerBlue = new(147, 165, 255);
    /// <summary>加载旋转器蓝色微光色（高亮变体）</summary>
    public static readonly RgbColor SpinnerBlueShimmer = new(177, 195, 255);

    #endregion

    #region Permission / Suggestion

    /// <summary>权限提示颜色</summary>
    public static readonly RgbColor Permission = new(177, 185, 249);
    /// <summary>权限提示微光色（高亮变体）</summary>
    public static readonly RgbColor PermissionShimmer = new(207, 215, 255);

    #endregion

    #region Semantic

    /// <summary>计划模式颜色</summary>
    public static readonly RgbColor PlanMode = new(72, 150, 140);
    /// <summary>IDE 集成模式颜色</summary>
    public static readonly RgbColor Ide = new(71, 130, 200);
    /// <summary>自动接受模式颜色</summary>
    public static readonly RgbColor AutoAccept = new(175, 135, 255);
    /// <summary>合并模式颜色</summary>
    public static readonly RgbColor Merged = new(175, 135, 255);
    /// <summary>快速模式颜色</summary>
    public static readonly RgbColor FastMode = new(255, 120, 20);
    /// <summary>快速模式微光色（高亮变体）</summary>
    public static readonly RgbColor FastModeShimmer = new(255, 165, 70);

    #endregion

    #region Text

    /// <summary>默认文本颜色</summary>
    public static readonly RgbColor Text = new(255, 255, 255);
    /// <summary>反色文本颜色</summary>
    public static readonly RgbColor InverseText = new(0, 0, 0);
    /// <summary>非激活状态文本颜色</summary>
    public static readonly RgbColor Inactive = new(153, 153, 153);
    /// <summary>非激活状态文本微光色（高亮变体）</summary>
    public static readonly RgbColor InactiveShimmer = new(193, 193, 193);
    /// <summary>柔和文本颜色（低对比度）</summary>
    public static readonly RgbColor Subtle = new(80, 80, 80);
    /// <summary>记忆提示文本颜色</summary>
    public static readonly RgbColor Remember = new(177, 185, 249);
    /// <summary>默认背景颜色</summary>
    public static readonly RgbColor Background = new(0, 0, 0);

    #endregion

    #region Status

    /// <summary>成功状态颜色</summary>
    public static readonly RgbColor StatusSuccess = new(78, 186, 101);
    /// <summary>错误状态颜色</summary>
    public static readonly RgbColor StatusError = new(255, 107, 128);
    /// <summary>警告状态颜色</summary>
    public static readonly RgbColor StatusWarning = new(255, 193, 7);
    /// <summary>警告状态微光色（高亮变体）</summary>
    public static readonly RgbColor StatusWarningShimmer = new(255, 223, 57);

    #endregion

    #region Prompt Border

    /// <summary>提示框边框颜色</summary>
    public static readonly RgbColor PromptBorder = new(136, 136, 136);
    /// <summary>提示框边框微光色（高亮变体）</summary>
    public static readonly RgbColor PromptBorderShimmer = new(166, 166, 166);

    #endregion

    #region Bash

    /// <summary>Bash 消息边框颜色</summary>
    public static readonly RgbColor BashBorder = new(253, 93, 177);
    /// <summary>Bash 消息背景颜色</summary>
    public static readonly RgbColor BashMessageBackground = new(65, 60, 65);

    #endregion

    #region Diff

    /// <summary>差异对比中新增行的颜色</summary>
    public static readonly RgbColor DiffAdded = new(34, 92, 43);
    /// <summary>差异对比中删除行的颜色</summary>
    public static readonly RgbColor DiffRemoved = new(122, 41, 54);
    /// <summary>差异对比中新增行的暗色（未聚焦变体）</summary>
    public static readonly RgbColor DiffAddedDimmed = new(71, 88, 74);
    /// <summary>差异对比中删除行的暗色（未聚焦变体）</summary>
    public static readonly RgbColor DiffRemovedDimmed = new(105, 72, 77);
    /// <summary>差异对比中新增单词的颜色（行内高亮）</summary>
    public static readonly RgbColor DiffAddedWord = new(56, 166, 96);
    /// <summary>差异对比中删除单词的颜色（行内高亮）</summary>
    public static readonly RgbColor DiffRemovedWord = new(179, 89, 107);

    #endregion

    #region SubAgent

    /// <summary>子代理红色标识</summary>
    public static readonly RgbColor SubAgentRed = new(220, 38, 38);
    /// <summary>子代理蓝色标识</summary>
    public static readonly RgbColor SubAgentBlue = new(37, 99, 235);
    /// <summary>子代理绿色标识</summary>
    public static readonly RgbColor SubAgentGreen = new(22, 163, 74);
    /// <summary>子代理黄色标识</summary>
    public static readonly RgbColor SubAgentYellow = new(202, 138, 4);
    /// <summary>子代理紫色标识</summary>
    public static readonly RgbColor SubAgentPurple = new(147, 51, 234);
    /// <summary>子代理橙色标识</summary>
    public static readonly RgbColor SubAgentOrange = new(234, 88, 12);
    /// <summary>子代理粉色标识</summary>
    public static readonly RgbColor SubAgentPink = new(219, 39, 119);
    /// <summary>子代理青色标识</summary>
    public static readonly RgbColor SubAgentCyan = new(8, 145, 178);

    #endregion

    #region Rainbow

    /// <summary>彩虹色系 — 红色</summary>
    public static readonly RgbColor RainbowRed = new(235, 95, 87);
    /// <summary>彩虹色系 — 橙色</summary>
    public static readonly RgbColor RainbowOrange = new(245, 139, 87);
    /// <summary>彩虹色系 — 黄色</summary>
    public static readonly RgbColor RainbowYellow = new(250, 195, 95);
    /// <summary>彩虹色系 — 绿色</summary>
    public static readonly RgbColor RainbowGreen = new(145, 200, 130);
    /// <summary>彩虹色系 — 蓝色</summary>
    public static readonly RgbColor RainbowBlue = new(130, 170, 220);
    /// <summary>彩虹色系 — 靛色</summary>
    public static readonly RgbColor RainbowIndigo = new(155, 130, 200);
    /// <summary>彩虹色系 — 紫色</summary>
    public static readonly RgbColor RainbowViolet = new(200, 130, 180);
    /// <summary>彩虹色系 — 红色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowRedShimmer = new(250, 155, 147);
    /// <summary>彩虹色系 — 橙色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowOrangeShimmer = new(255, 185, 137);
    /// <summary>彩虹色系 — 黄色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowYellowShimmer = new(255, 225, 155);
    /// <summary>彩虹色系 — 绿色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowGreenShimmer = new(185, 230, 180);
    /// <summary>彩虹色系 — 蓝色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowBlueShimmer = new(180, 205, 240);
    /// <summary>彩虹色系 — 靛色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowIndigoShimmer = new(195, 180, 230);
    /// <summary>彩虹色系 — 紫色微光色（高亮变体）</summary>
    public static readonly RgbColor RainbowVioletShimmer = new(230, 180, 210);

    #endregion

    #region TUI V2 / Message Backgrounds

    /// <summary>Clawd 主体颜色</summary>
    public static readonly RgbColor ClawdBody = new(215, 119, 87);
    /// <summary>Clawd 背景颜色</summary>
    public static readonly RgbColor ClawdBackground = new(0, 0, 0);
    /// <summary>用户消息背景颜色</summary>
    public static readonly RgbColor UserMessageBackground = new(55, 55, 55);
    /// <summary>用户消息背景悬停颜色（高亮变体）</summary>
    public static readonly RgbColor UserMessageBackgroundHover = new(70, 70, 70);
    /// <summary>消息操作区背景颜色</summary>
    public static readonly RgbColor MessageActionsBackground = new(44, 50, 62);
    /// <summary>选中文本背景颜色</summary>
    public static readonly RgbColor SelectionBackground = new(38, 79, 120);
    /// <summary>记忆面板背景颜色</summary>
    public static readonly RgbColor MemoryBackground = new(55, 65, 70);

    #endregion

    #region Rate Limit

    /// <summary>速率限制进度条已用部分颜色</summary>
    public static readonly RgbColor RateLimitFill = new(177, 185, 249);
    /// <summary>速率限制进度条剩余部分颜色</summary>
    public static readonly RgbColor RateLimitEmpty = new(80, 83, 112);

    #endregion

    #region Brief Labels

    /// <summary>简要标签"你"（用户）的颜色</summary>
    public static readonly RgbColor BriefLabelYou = new(122, 180, 232);
    /// <summary>简要标签"Claude"（助手）的颜色</summary>
    public static readonly RgbColor BriefLabelClaude = new(215, 119, 87);

    #endregion

    #region Search

    /// <summary>搜索匹配项高亮颜色</summary>
    public static readonly RgbColor SearchMatch = new(255, 215, 0);
    /// <summary>搜索当前匹配项高亮颜色</summary>
    public static readonly RgbColor SearchMatchCurrent = new(255, 165, 0);

    #endregion

    #region Misc

    /// <summary>专业风格蓝色</summary>
    public static readonly RgbColor ProfessionalBlue = new(106, 155, 204);
    /// <summary>Chrome 风格黄色</summary>
    public static readonly RgbColor ChromeYellow = new(251, 188, 4);

    #endregion

    #region Backward-Compatible String Properties

    /// <summary>主色 ANSI 前景转义字符串</summary>
    public static string Primary => JoinCode.ToAnsiFg();
    /// <summary>强调色 ANSI 前景转义字符串</summary>
    public static string Accent => Permission.ToAnsiFg();
    /// <summary>次要色 ANSI 前景转义字符串</summary>
    public static string Secondary => Permission.ToAnsiFg();
    /// <summary>分隔线 ANSI 前景转义字符串</summary>
    public static string Divider => Subtle.ToAnsiFg();
    /// <summary>柔和色 ANSI 前景转义字符串</summary>
    public static string Muted => Inactive.ToAnsiFg();
    /// <summary>成功色 ANSI 前景转义字符串</summary>
    public static string Success => new RgbColor(78, 186, 101).ToAnsiFg();
    /// <summary>警告色 ANSI 前景转义字符串</summary>
    public static string Warning => new RgbColor(255, 193, 7).ToAnsiFg();
    /// <summary>错误色 ANSI 前景转义字符串</summary>
    public static string Error => new RgbColor(255, 107, 128).ToAnsiFg();
    /// <summary>信息色 ANSI 前景转义字符串</summary>
    public static string Info => PlanMode.ToAnsiFg();

    #endregion
}