namespace JoinCode.Cli;

/// <summary>
/// 终端颜色常量 — CLI 模式下的 ANSI 颜色定义
/// </summary>
public static class TerminalColors
{
    #region Brand

    public static readonly RgbColor Claude = new(215, 119, 87);
    public static readonly RgbColor ClaudeShimmer = new(235, 159, 127);

    #endregion

    #region Spinner

    public static readonly RgbColor SpinnerBlue = new(147, 165, 255);
    public static readonly RgbColor SpinnerBlueShimmer = new(177, 195, 255);

    #endregion

    #region Permission / Suggestion

    public static readonly RgbColor Permission = new(177, 185, 249);
    public static readonly RgbColor PermissionShimmer = new(207, 215, 255);

    #endregion

    #region Semantic

    public static readonly RgbColor PlanMode = new(72, 150, 140);
    public static readonly RgbColor Ide = new(71, 130, 200);
    public static readonly RgbColor AutoAccept = new(175, 135, 255);
    public static readonly RgbColor Merged = new(175, 135, 255);
    public static readonly RgbColor FastMode = new(255, 120, 20);
    public static readonly RgbColor FastModeShimmer = new(255, 165, 70);

    #endregion

    #region Text

    public static readonly RgbColor Text = new(255, 255, 255);
    public static readonly RgbColor InverseText = new(0, 0, 0);
    public static readonly RgbColor Inactive = new(153, 153, 153);
    public static readonly RgbColor InactiveShimmer = new(193, 193, 193);
    public static readonly RgbColor Subtle = new(80, 80, 80);
    public static readonly RgbColor Remember = new(177, 185, 249);
    public static readonly RgbColor Background = new(0, 0, 0);

    #endregion

    #region Status

    public static readonly RgbColor StatusSuccess = new(78, 186, 101);
    public static readonly RgbColor StatusError = new(255, 107, 128);
    public static readonly RgbColor StatusWarning = new(255, 193, 7);
    public static readonly RgbColor StatusWarningShimmer = new(255, 223, 57);

    #endregion

    #region Prompt Border

    public static readonly RgbColor PromptBorder = new(136, 136, 136);
    public static readonly RgbColor PromptBorderShimmer = new(166, 166, 166);

    #endregion

    #region Bash

    public static readonly RgbColor BashBorder = new(253, 93, 177);
    public static readonly RgbColor BashMessageBackground = new(65, 60, 65);

    #endregion

    #region Diff

    public static readonly RgbColor DiffAdded = new(34, 92, 43);
    public static readonly RgbColor DiffRemoved = new(122, 41, 54);
    public static readonly RgbColor DiffAddedDimmed = new(71, 88, 74);
    public static readonly RgbColor DiffRemovedDimmed = new(105, 72, 77);
    public static readonly RgbColor DiffAddedWord = new(56, 166, 96);
    public static readonly RgbColor DiffRemovedWord = new(179, 89, 107);

    #endregion

    #region SubAgent

    public static readonly RgbColor SubAgentRed = new(220, 38, 38);
    public static readonly RgbColor SubAgentBlue = new(37, 99, 235);
    public static readonly RgbColor SubAgentGreen = new(22, 163, 74);
    public static readonly RgbColor SubAgentYellow = new(202, 138, 4);
    public static readonly RgbColor SubAgentPurple = new(147, 51, 234);
    public static readonly RgbColor SubAgentOrange = new(234, 88, 12);
    public static readonly RgbColor SubAgentPink = new(219, 39, 119);
    public static readonly RgbColor SubAgentCyan = new(8, 145, 178);

    #endregion

    #region Rainbow

    public static readonly RgbColor RainbowRed = new(235, 95, 87);
    public static readonly RgbColor RainbowOrange = new(245, 139, 87);
    public static readonly RgbColor RainbowYellow = new(250, 195, 95);
    public static readonly RgbColor RainbowGreen = new(145, 200, 130);
    public static readonly RgbColor RainbowBlue = new(130, 170, 220);
    public static readonly RgbColor RainbowIndigo = new(155, 130, 200);
    public static readonly RgbColor RainbowViolet = new(200, 130, 180);
    public static readonly RgbColor RainbowRedShimmer = new(250, 155, 147);
    public static readonly RgbColor RainbowOrangeShimmer = new(255, 185, 137);
    public static readonly RgbColor RainbowYellowShimmer = new(255, 225, 155);
    public static readonly RgbColor RainbowGreenShimmer = new(185, 230, 180);
    public static readonly RgbColor RainbowBlueShimmer = new(180, 205, 240);
    public static readonly RgbColor RainbowIndigoShimmer = new(195, 180, 230);
    public static readonly RgbColor RainbowVioletShimmer = new(230, 180, 210);

    #endregion

    #region TUI V2 / Message Backgrounds

    public static readonly RgbColor ClawdBody = new(215, 119, 87);
    public static readonly RgbColor ClawdBackground = new(0, 0, 0);
    public static readonly RgbColor UserMessageBackground = new(55, 55, 55);
    public static readonly RgbColor UserMessageBackgroundHover = new(70, 70, 70);
    public static readonly RgbColor MessageActionsBackground = new(44, 50, 62);
    public static readonly RgbColor SelectionBackground = new(38, 79, 120);
    public static readonly RgbColor MemoryBackground = new(55, 65, 70);

    #endregion

    #region Rate Limit

    public static readonly RgbColor RateLimitFill = new(177, 185, 249);
    public static readonly RgbColor RateLimitEmpty = new(80, 83, 112);

    #endregion

    #region Brief Labels

    public static readonly RgbColor BriefLabelYou = new(122, 180, 232);
    public static readonly RgbColor BriefLabelClaude = new(215, 119, 87);

    #endregion

    #region Search

    public static readonly RgbColor SearchMatch = new(255, 215, 0);
    public static readonly RgbColor SearchMatchCurrent = new(255, 165, 0);

    #endregion

    #region Misc

    public static readonly RgbColor ProfessionalBlue = new(106, 155, 204);
    public static readonly RgbColor ChromeYellow = new(251, 188, 4);

    #endregion

    #region Backward-Compatible String Properties

    public static string Primary => Claude.ToAnsiFg();
    public static string Accent => Permission.ToAnsiFg();
    public static string Secondary => Permission.ToAnsiFg();
    public static string Divider => Subtle.ToAnsiFg();
    public static string Muted => Inactive.ToAnsiFg();
    public static string Success => new RgbColor(78, 186, 101).ToAnsiFg();
    public static string Warning => new RgbColor(255, 193, 7).ToAnsiFg();
    public static string Error => new RgbColor(255, 107, 128).ToAnsiFg();
    public static string Info => PlanMode.ToAnsiFg();

    #endregion
}
