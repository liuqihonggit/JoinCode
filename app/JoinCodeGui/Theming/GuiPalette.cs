using Avalonia.Media;
using Avalonia.Controls;

namespace JoinCode.Gui.Theming;

/// <summary>
/// 主程序 UI 语义配色单一数据源。
/// 所有颜色 —— 背景面/文字/气泡/指示器 —— 都必须从本类取值，禁止在 XAML 或转换器中写死十六进制。
/// 支持按 <see cref="GuiThemeVariant"/> 提供明/暗两套（可扩展更多主题）；切换时以 <c>ThemeDictionaries</c> 形式注入
/// 应用资源字典，使 XAML 中 {DynamicResource} 自动解析对应主题的 <see cref="IBrush"/>。
/// </summary>
public static class GuiPalette
{
    /// <summary>主题变体：Dark（默认）/ Light，可扩展未来主题。</summary>
    public enum GuiThemeVariant { Dark, Light }

    /// <summary>一组语义颜色（单个主题的静态数据）。</summary>
    public sealed class Scheme
    {
        public string WindowBackground { get; init; } = "#1e1e1e";
        public string SidebarBackground { get; init; } = "#161616";
        public string SidebarTitle { get; init; } = "#eeeeee";
        public string TopBarBackground { get; init; } = "#252525";
        public string InputBarBackground { get; init; } = "#2a2a2a";
        public string StatusBarBackground { get; init; } = "#181818";
        public string SearchBarBackground { get; init; } = "#202020";
        public string SettingsBackground { get; init; } = "#1a1a1a";
        public string Divider { get; init; } = "#333333";
        public string PrimaryText { get; init; } = "#e0e0e0";
        public string SecondaryText { get; init; } = "#b8b8b8";
        public string MutedText { get; init; } = "#979797";
        public string AccentText { get; init; } = "#4da6ff";
        public string RoleUser { get; init; } = "#4da6ff";
        public string RoleAssistant { get; init; } = "#9cdcfe";
        public string BubbleText { get; init; } = "#333333";
        public string BubbleUser { get; init; } = "#2b3a4a";
        public string BubbleThinking { get; init; } = "#26222e";
        public string BubbleToolCall { get; init; } = "#1c2836";
        public string BubbleToolResult { get; init; } = "#1e2c26";
        public string ThinkingLabel { get; init; } = "#b48fe0";
        public string ToolLabel { get; init; } = "#6ab";
        public string ToolArgument { get; init; } = "#89a";
        public string ToolResult { get; init; } = "#8a9";
        public string WarnText { get; init; } = "#e5484d";
        public string ErrorText { get; init; } = "#e5484d";
        public string SuccessText { get; init; } = "#3dd68c";
        public string BusyText { get; init; } = "#ffaa33";
        public string SessionHighlight { get; init; } = "#3a4a5a";
        public string ButtonBackground { get; init; } = "#2b2b2b";
        public string ButtonHover { get; init; } = "#353535";
        public string ButtonPressed { get; init; } = "#3d3d3d";
        public string ButtonBorder { get; init; } = "#3a3a3a";
        public string ButtonForeground { get; init; } = "#e0e0e0";
        public string EditorForeground { get; init; } = "#D4D4D4";
        public string ToastSuccess { get; init; } = "#4a9eff";
        public string ToastError { get; init; } = "#d43a3a";
        public string ToastShadow { get; init; } = "#90000000";
        public string SlashMatched { get; init; } = "#E89A3C";
        public string ToastForeground { get; init; } = "#FFFFFF";

        /// <summary>弹层背景（补全面板等浮层）— 比窗口底色略抬升制造层次</summary>
        public string PopupBackground { get; init; } = "#232327";

        /// <summary>补全面板选中行背景 — accent 蓝的低饱和暗色调，选中态醒目但不刺眼</summary>
        public string PaletteSelectedRow { get; init; } = "#2c3a4d";

        /// <summary>accent 淡底（主操作弱化态/药丸建议/模型徽章）</summary>
        public string AccentSubtle { get; init; } = "#1c2e44";

        /// <summary>accent 淡底悬停态</summary>
        public string AccentSubtleHover { get; init; } = "#243a55";

        /// <summary>primary 按钮（accent 实底）悬停态</summary>
        public string AccentHover { get; init; } = "#63b1ff";

        /// <summary>消息卡片悬停态背景</summary>
        public string CardHover { get; init; } = "#242429";

        /// <summary>输入栏内嵌 composer 卡片表面色（无边框 TextBox + 发送按钮的承载卡片）</summary>
        public string ComposerBackground { get; init; } = "#202020";

        /// <summary>Markdown 代码块背景（暗色深灰 / 亮色浅灰，保证代码文字两主题均可读）</summary>
        public string CodeBlockBackground { get; init; } = "#141414";

        /// <summary>Diff 新增行背景（绿系弱底）</summary>
        public string DiffAddedBackground { get; init; } = "#1a2e22";

        /// <summary>Diff 删除行背景（红系弱底）</summary>
        public string DiffRemovedBackground { get; init; } = "#2e1a1a";

        /// <summary>遍历全部 token 值，供对比度校验与资源注入使用。</summary>
        public IEnumerable<string> AllTokens()
        {
            yield return WindowBackground;
            yield return SidebarBackground;
            yield return SidebarTitle;
            yield return TopBarBackground;
            yield return InputBarBackground;
            yield return StatusBarBackground;
            yield return SearchBarBackground;
            yield return SettingsBackground;
            yield return Divider;
            yield return PrimaryText;
            yield return SecondaryText;
            yield return MutedText;
            yield return AccentText;
            yield return RoleUser;
            yield return RoleAssistant;
            yield return BubbleText;
            yield return BubbleUser;
            yield return BubbleThinking;
            yield return BubbleToolCall;
            yield return BubbleToolResult;
            yield return ThinkingLabel;
            yield return ToolLabel;
            yield return ToolArgument;
            yield return ToolResult;
            yield return WarnText;
            yield return ErrorText;
            yield return SuccessText;
            yield return BusyText;
            yield return SessionHighlight;
            yield return ButtonBackground;
            yield return ButtonHover;
            yield return ButtonPressed;
            yield return ButtonBorder;
            yield return ButtonForeground;
            yield return EditorForeground;
            yield return ToastSuccess;
            yield return ToastError;
            yield return ToastShadow;
            yield return SlashMatched;
            yield return ToastForeground;
            yield return PopupBackground;
            yield return PaletteSelectedRow;
            yield return AccentSubtle;
            yield return AccentSubtleHover;
            yield return AccentHover;
            yield return CardHover;
            yield return ComposerBackground;
            yield return CodeBlockBackground;
            yield return DiffAddedBackground;
            yield return DiffRemovedBackground;
        }
    }

    private static readonly Scheme Dark = new();
    private static readonly Scheme Light = new()
    {
        WindowBackground = "#f5f5f5",
        SidebarBackground = "#ececec",
        SidebarTitle = "#1f1f1f",
        TopBarBackground = "#e8e8e8",
        InputBarBackground = "#e3e3e3",
        StatusBarBackground = "#ebebeb",
        SearchBarBackground = "#e8e8e8",
        SettingsBackground = "#efefef",
        Divider = "#c9c9c9",
        PrimaryText = "#1c1c1c",
        SecondaryText = "#3f3f3f",
        MutedText = "#5f5f5f",
        AccentText = "#1a6bc0",
        RoleUser = "#1a6bc0",
        RoleAssistant = "#0e7490",
        BubbleText = "#ffffff",
        BubbleUser = "#d5e6ff",
        BubbleThinking = "#ece6f4",
        BubbleToolCall = "#dbe8f7",
        BubbleToolResult = "#dcf0e2",
        ThinkingLabel = "#5b3f86",
        ToolLabel = "#15579e",
        ToolArgument = "#4a6478",
        ToolResult = "#22663f",
        WarnText = "#c62828",
        ErrorText = "#c62828",
        SuccessText = "#1a7f37",
        BusyText = "#b35c00",
        SessionHighlight = "#c9c9e0",
        ButtonBackground = "#ffffff",
        ButtonHover = "#eef1f5",
        ButtonPressed = "#e3e7ec",
        ButtonBorder = "#c9c9c9",
        ButtonForeground = "#1c1c1c",
        EditorForeground = "#1c1c1c",
        ToastSuccess = "#1a6bc0",
        ToastError = "#c62828",
        ToastShadow = "#90000000",
        SlashMatched = "#B35C00",
        ToastForeground = "#FFFFFF",
        PopupBackground = "#ffffff",
        PaletteSelectedRow = "#d8e4f2",
        AccentSubtle = "#dce9f8",
        AccentSubtleHover = "#cfe0f5",
        AccentHover = "#2f7fd4",
        CardHover = "#e9e9e9",
        ComposerBackground = "#ffffff",
        CodeBlockBackground = "#ececec",
        DiffAddedBackground = "#dcf0e2",
        DiffRemovedBackground = "#f7dcdc"
    };

    /// <summary>获取指定主题的配色方案。</summary>
    public static Scheme SchemeFor(GuiThemeVariant variant)
        => variant == GuiThemeVariant.Light ? Light : Dark;

    private static GuiThemeVariant _currentVariant = GuiThemeVariant.Dark;

    /// <summary>当前生效主题变体（由主窗口在切换时更新，转换器据此取色）。</summary>
    public static GuiThemeVariant CurrentVariant
    {
        get => _currentVariant;
        set => _currentVariant = value;
    }

    /// <summary>当前主题配色方案。</summary>
    public static Scheme Current => SchemeFor(_currentVariant);

    /// <summary>以"主题 → Brush 资源字典"的形式生成 ThemeDictionaries，供 Application 注入。</summary>
    public static IReadOnlyDictionary<GuiThemeVariant, ResourceDictionary> BuildResourceDictionaries()
    {
        var result = new Dictionary<GuiThemeVariant, ResourceDictionary>
        {
            [GuiThemeVariant.Dark] = BuildDictionary(Dark),
            [GuiThemeVariant.Light] = BuildDictionary(Light)
        };
        return result;
    }

    private static ResourceDictionary BuildDictionary(Scheme scheme)
    {
        var dict = new ResourceDictionary();
        foreach (var (key, value) in SemanticTuples(scheme))
            dict[key] = ToBrush(value);
        return dict;
    }

    private static IEnumerable<(string Key, string Value)> SemanticTuples(Scheme s)
    {
        yield return ("GuiWindowBackground", s.WindowBackground);
        yield return ("GuiSidebarBackground", s.SidebarBackground);
        yield return ("GuiSidebarTitle", s.SidebarTitle);
        yield return ("GuiTopBarBackground", s.TopBarBackground);
        yield return ("GuiInputBarBackground", s.InputBarBackground);
        yield return ("GuiStatusBarBackground", s.StatusBarBackground);
        yield return ("GuiSearchBarBackground", s.SearchBarBackground);
        yield return ("GuiSettingsBackground", s.SettingsBackground);
        yield return ("GuiDivider", s.Divider);
        yield return ("GuiPrimaryText", s.PrimaryText);
        yield return ("GuiSecondaryText", s.SecondaryText);
        yield return ("GuiMutedText", s.MutedText);
        yield return ("GuiAccentText", s.AccentText);
        yield return ("GuiRoleUser", s.RoleUser);
        yield return ("GuiRoleAssistant", s.RoleAssistant);
        yield return ("GuiBubbleText", s.BubbleText);
        yield return ("GuiBubbleUser", s.BubbleUser);
        yield return ("GuiBubbleThinking", s.BubbleThinking);
        yield return ("GuiBubbleToolCall", s.BubbleToolCall);
        yield return ("GuiBubbleToolResult", s.BubbleToolResult);
        yield return ("GuiThinkingLabel", s.ThinkingLabel);
        yield return ("GuiToolLabel", s.ToolLabel);
        yield return ("GuiToolArgument", s.ToolArgument);
        yield return ("GuiToolResult", s.ToolResult);
        yield return ("GuiWarnText", s.WarnText);
        yield return ("GuiErrorText", s.ErrorText);
        yield return ("GuiSuccessText", s.SuccessText);
        yield return ("GuiBusyText", s.BusyText);
        yield return ("GuiSessionHighlight", s.SessionHighlight);
        yield return ("GuiButtonBackground", s.ButtonBackground);
        yield return ("GuiButtonHover", s.ButtonHover);
        yield return ("GuiButtonPressed", s.ButtonPressed);
        yield return ("GuiButtonBorder", s.ButtonBorder);
        yield return ("GuiButtonForeground", s.ButtonForeground);
        yield return ("GuiEditorForeground", s.EditorForeground);
        yield return ("GuiToastSuccess", s.ToastSuccess);
        yield return ("GuiToastError", s.ToastError);
        yield return ("GuiToastShadow", s.ToastShadow);
        yield return ("GuiSlashMatched", s.SlashMatched);
        yield return ("GuiToastForeground", s.ToastForeground);
        yield return ("GuiPopupBackground", s.PopupBackground);
        yield return ("GuiPaletteSelectedRow", s.PaletteSelectedRow);
        yield return ("GuiAccentSubtle", s.AccentSubtle);
        yield return ("GuiAccentSubtleHover", s.AccentSubtleHover);
        yield return ("GuiAccentHover", s.AccentHover);
            yield return ("GuiCardHover", s.CardHover);
            yield return ("GuiComposerBackground", s.ComposerBackground);
            yield return ("GuiCodeBlockBackground", s.CodeBlockBackground);
            yield return ("GuiDiffAddedBackground", s.DiffAddedBackground);
            yield return ("GuiDiffRemovedBackground", s.DiffRemovedBackground);
    }

    /// <summary>解析十六进制色为不可变画刷（供资源和转换器共用）。</summary>
    public static ISolidColorBrush ToBrush(string hex)
        => new SolidColorBrush(Color.Parse(hex));

    /// <summary>计算两色 WCAG 相对亮度。</summary>
    public static double RelativeLuminance(Color c)
    {
        double L(double v) => v <= 0.03928
            ? v / 12.92
            : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R / 255.0) + 0.7152 * L(c.G / 255.0) + 0.0722 * L(c.B / 255.0);
    }

    /// <summary>计算两色对比度（1 到 21）。</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }
}