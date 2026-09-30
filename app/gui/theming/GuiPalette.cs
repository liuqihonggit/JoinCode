namespace JoinCode.Gui.Theming;

/// <summary>
/// 主程序 UI 语义配色单一数据源。
/// 所有颜色 —— 背景面/文字/气泡/指示器 —— 都必须从本类取值，禁止在 XAML 或转换器中写死十六进制。
/// 支持按 <see cref="GuiThemeVariant"/> 提供明/暗两套（可扩展更多主题）；切换时以 <c>ThemeDictionaries</c> 形式注入
/// 应用资源字典，使 XAML 中 {DynamicResource} 自动解析对应主题的 <see cref="IBrush"/>。
/// </summary>
public static class GuiPalette {
    /// <summary>主题变体：Dark（默认）/ Light / SolarizedDark / SolarizedLight。</summary>
    public enum GuiThemeVariant {
        /// <summary>暗色主题（默认）</summary>
        [EnumValue("dark")]
        Dark,
        /// <summary>亮色主题</summary>
        [EnumValue("light")]
        Light,
        /// <summary>Solarized 暗色主题（经典程序员配色）</summary>
        [EnumValue("solarized-dark")]
        SolarizedDark,
        /// <summary>Solarized 亮色主题</summary>
        [EnumValue("solarized-light")]
        SolarizedLight,
    }

    /// <summary>一组语义颜色（单个主题的静态数据）。</summary>
    public sealed class Scheme {
        /// <summary>窗口背景色</summary>
        public string WindowBackground { get; init; } = "#1e1e1e";
        /// <summary>侧边栏背景色</summary>
        public string SidebarBackground { get; init; } = "#161616";
        /// <summary>侧边栏标题色</summary>
        public string SidebarTitle { get; init; } = "#eeeeee";
        /// <summary>顶栏背景色</summary>
        public string TopBarBackground { get; init; } = "#252525";
        /// <summary>输入栏背景色</summary>
        public string InputBarBackground { get; init; } = "#2a2a2a";
        /// <summary>状态栏背景色</summary>
        public string StatusBarBackground { get; init; } = "#181818";
        /// <summary>搜索栏背景色</summary>
        public string SearchBarBackground { get; init; } = "#202020";
        /// <summary>设置面板背景色</summary>
        public string SettingsBackground { get; init; } = "#1a1a1a";
        /// <summary>分隔线颜色</summary>
        public string Divider { get; init; } = "#333333";
        /// <summary>主要文字色</summary>
        public string PrimaryText { get; init; } = "#e0e0e0";
        /// <summary>次要文字色</summary>
        public string SecondaryText { get; init; } = "#b8b8b8";
        /// <summary>弱化文字色</summary>
        public string MutedText { get; init; } = "#979797";
        /// <summary>强调文字色</summary>
        public string AccentText { get; init; } = "#4da6ff";
        /// <summary>用户角色色</summary>
        public string RoleUser { get; init; } = "#4da6ff";
        /// <summary>助手角色色</summary>
        public string RoleAssistant { get; init; } = "#9cdcfe";
        /// <summary>气泡文字色</summary>
        public string BubbleText { get; init; } = "#333333";
        /// <summary>用户气泡背景色</summary>
        public string BubbleUser { get; init; } = "#2b3a4a";
        /// <summary>思考气泡背景色</summary>
        public string BubbleThinking { get; init; } = "#26222e";
        /// <summary>工具调用气泡背景色</summary>
        public string BubbleToolCall { get; init; } = "#1c2836";
        /// <summary>工具结果气泡背景色</summary>
        public string BubbleToolResult { get; init; } = "#1e2c26";
        /// <summary>思考标签色</summary>
        public string ThinkingLabel { get; init; } = "#b48fe0";
        /// <summary>工具标签色</summary>
        public string ToolLabel { get; init; } = "#6ab";
        /// <summary>工具参数色</summary>
        public string ToolArgument { get; init; } = "#89a";
        /// <summary>工具结果色</summary>
        public string ToolResult { get; init; } = "#8a9";
        /// <summary>警告文字色</summary>
        public string WarnText { get; init; } = "#e5484d";
        /// <summary>错误文字色</summary>
        public string ErrorText { get; init; } = "#e5484d";
        /// <summary>成功文字色</summary>
        public string SuccessText { get; init; } = "#3dd68c";
        /// <summary>忙碌文字色</summary>
        public string BusyText { get; init; } = "#ffaa33";
        /// <summary>会话高亮色</summary>
        public string SessionHighlight { get; init; } = "#3a4a5a";
        /// <summary>按钮背景色</summary>
        public string ButtonBackground { get; init; } = "#2b2b2b";
        /// <summary>按钮悬停色</summary>
        public string ButtonHover { get; init; } = "#353535";
        /// <summary>按钮按下色</summary>
        public string ButtonPressed { get; init; } = "#3d3d3d";
        /// <summary>按钮边框色</summary>
        public string ButtonBorder { get; init; } = "#3a3a3a";
        /// <summary>按钮前景色</summary>
        public string ButtonForeground { get; init; } = "#e0e0e0";
        /// <summary>编辑器前景色</summary>
        public string EditorForeground { get; init; } = "#D4D4D4";
        /// <summary>成功提示色</summary>
        public string ToastSuccess { get; init; } = "#4a9eff";
        /// <summary>错误提示色</summary>
        public string ToastError { get; init; } = "#d43a3a";
        /// <summary>提示阴影色</summary>
        public string ToastShadow { get; init; } = "#90000000";
        /// <summary>斜杠命令匹配色</summary>
        public string SlashMatched { get; init; } = "#E89A3C";
        /// <summary>提示前景色</summary>
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

        /// <summary>危险等级黄灯色（Unknown — 未知命令需确认）</summary>
        public string DangerLevelYellow { get; init; } = "#ffc107";

        /// <summary>危险等级绿灯色（LightValidation — 可撤回操作）</summary>
        public string DangerLevelGreen { get; init; } = "#3dd68c";

        /// <summary>危险等级红灯色（Execution — 执行/不可撤回操作）</summary>
        public string DangerLevelRed { get; init; } = "#e5484d";

        /// <summary>危险等级黑灯色（Dangerous — 危险操作深红）</summary>
        public string DangerLevelBlack { get; init; } = "#8b0000";

        /// <summary>轮次互补色 A（偶数轮 — 深蓝调）— 驱动轮次色条，区分对话轮次（任务4）</summary>
        public string TurnColorA { get; init; } = "#2b3a52";

        /// <summary>轮次互补色 B（奇数轮 — 深琥珀调，A 的色轮互补）— 驱动轮次色条（任务4）</summary>
        public string TurnColorB { get; init; } = "#52402b";

        /// <summary>遍历全部 token 值，供对比度校验与资源注入使用。</summary>
        public IEnumerable<string> AllTokens() {
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
            yield return DangerLevelYellow;
            yield return DangerLevelGreen;
            yield return DangerLevelRed;
            yield return DangerLevelBlack;
            yield return TurnColorA;
            yield return TurnColorB;
        }
    }

    private static readonly Scheme Dark = new();
    private static readonly Scheme Light = new() {
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
        DiffRemovedBackground = "#f7dcdc",
        DangerLevelYellow = "#b35c00",
        DangerLevelGreen = "#1a7f37",
        DangerLevelRed = "#c62828",
        DangerLevelBlack = "#5d0000",
        TurnColorA = "#d5e6ff",
        TurnColorB = "#fff0d5"
    };

    /// <summary>Solarized Dark — 经典程序员暗色配色（base03 底色，base1 正文，blue 强调）</summary>
    private static readonly Scheme SolarizedDark = new() {
        WindowBackground = "#002b36",
        SidebarBackground = "#073642",
        SidebarTitle = "#93a1a1",
        TopBarBackground = "#073642",
        InputBarBackground = "#073642",
        StatusBarBackground = "#002b36",
        SearchBarBackground = "#073642",
        SettingsBackground = "#002b36",
        Divider = "#586e75",
        PrimaryText = "#93a1a1",
        SecondaryText = "#839496",
        MutedText = "#657b83",
        AccentText = "#268bd2",
        RoleUser = "#268bd2",
        RoleAssistant = "#2aa198",
        BubbleText = "#073642",
        BubbleUser = "#073642",
        BubbleThinking = "#073642",
        BubbleToolCall = "#073642",
        BubbleToolResult = "#073642",
        ThinkingLabel = "#6c71c4",
        ToolLabel = "#268bd2",
        ToolArgument = "#839496",
        ToolResult = "#859900",
        WarnText = "#cb4b16",
        ErrorText = "#dc322f",
        SuccessText = "#859900",
        BusyText = "#b58900",
        SessionHighlight = "#073642",
        ButtonBackground = "#073642",
        ButtonHover = "#094858",
        ButtonPressed = "#0a5060",
        ButtonBorder = "#586e75",
        ButtonForeground = "#93a1a1",
        EditorForeground = "#93a1a1",
        ToastSuccess = "#268bd2",
        ToastError = "#dc322f",
        ToastShadow = "#90000000",
        SlashMatched = "#b58900",
        ToastForeground = "#93a1a1",
        PopupBackground = "#073642",
        PaletteSelectedRow = "#094858",
        AccentSubtle = "#094858",
        AccentSubtleHover = "#0a5060",
        AccentHover = "#3a9bd8",
        CardHover = "#094858",
        ComposerBackground = "#073642",
        CodeBlockBackground = "#003845",
        DiffAddedBackground = "#073642",
        DiffRemovedBackground = "#073642",
        DangerLevelYellow = "#b58900",
        DangerLevelGreen = "#859900",
        DangerLevelRed = "#dc322f",
        DangerLevelBlack = "#851501",
        TurnColorA = "#094858",
        TurnColorB = "#584832"
    };

    /// <summary>Solarized Light — 经典程序员亮色配色（base3 底色，base00 正文，blue 强调）</summary>
    private static readonly Scheme SolarizedLight = new() {
        WindowBackground = "#fdf6e3",
        SidebarBackground = "#eee8d5",
        SidebarTitle = "#586e75",
        TopBarBackground = "#eee8d5",
        InputBarBackground = "#eee8d5",
        StatusBarBackground = "#fdf6e3",
        SearchBarBackground = "#eee8d5",
        SettingsBackground = "#fdf6e3",
        Divider = "#93a1a1",
        PrimaryText = "#657b83",
        SecondaryText = "#586e75",
        MutedText = "#93a1a1",
        AccentText = "#268bd2",
        RoleUser = "#268bd2",
        RoleAssistant = "#2aa198",
        BubbleText = "#586e75",
        BubbleUser = "#eee8d5",
        BubbleThinking = "#eee8d5",
        BubbleToolCall = "#eee8d5",
        BubbleToolResult = "#eee8d5",
        ThinkingLabel = "#6c71c4",
        ToolLabel = "#268bd2",
        ToolArgument = "#586e75",
        ToolResult = "#859900",
        WarnText = "#cb4b16",
        ErrorText = "#dc322f",
        SuccessText = "#859900",
        BusyText = "#b58900",
        SessionHighlight = "#eee8d5",
        ButtonBackground = "#eee8d5",
        ButtonHover = "#e0d8c4",
        ButtonPressed = "#d6cdb6",
        ButtonBorder = "#93a1a1",
        ButtonForeground = "#657b83",
        EditorForeground = "#657b83",
        ToastSuccess = "#268bd2",
        ToastError = "#dc322f",
        ToastShadow = "#90000000",
        SlashMatched = "#b58900",
        ToastForeground = "#657b83",
        PopupBackground = "#eee8d5",
        PaletteSelectedRow = "#e0d8c4",
        AccentSubtle = "#e0d8c4",
        AccentSubtleHover = "#d6cdb6",
        AccentHover = "#1a7fc0",
        CardHover = "#e0d8c4",
        ComposerBackground = "#eee8d5",
        CodeBlockBackground = "#eee8d5",
        DiffAddedBackground = "#eee8d5",
        DiffRemovedBackground = "#eee8d5",
        DangerLevelYellow = "#b58900",
        DangerLevelGreen = "#859900",
        DangerLevelRed = "#dc322f",
        DangerLevelBlack = "#851501",
        TurnColorA = "#d5e6ff",
        TurnColorB = "#fff0d5"
    };

    /// <summary>获取指定主题的配色方案。</summary>
    public static Scheme SchemeFor(GuiThemeVariant variant) => variant switch {
        GuiThemeVariant.Light => Light,
        GuiThemeVariant.SolarizedDark => SolarizedDark,
        GuiThemeVariant.SolarizedLight => SolarizedLight,
        _ => Dark
    };

    private static GuiThemeVariant _currentVariant = GuiThemeVariant.Dark;

    /// <summary>当前生效主题变体（由主窗口在切换时更新，转换器据此取色）。</summary>
    public static GuiThemeVariant CurrentVariant {
        get => _currentVariant;
        set => _currentVariant = value;
    }

    /// <summary>当前主题配色方案。</summary>
    public static Scheme Current => SchemeFor(_currentVariant);

    /// <summary>以"主题 → Brush 资源字典"的形式生成 ThemeDictionaries，供 Application 注入。</summary>
    public static IReadOnlyDictionary<GuiThemeVariant, ResourceDictionary> BuildResourceDictionaries() {
        var result = new Dictionary<GuiThemeVariant, ResourceDictionary> {
            [GuiThemeVariant.Dark] = BuildDictionary(Dark),
            [GuiThemeVariant.Light] = BuildDictionary(Light),
            [GuiThemeVariant.SolarizedDark] = BuildDictionary(SolarizedDark),
            [GuiThemeVariant.SolarizedLight] = BuildDictionary(SolarizedLight)
        };
        return result;
    }

    /// <summary>构建指定主题的资源字典（供 GuiAppResources 主题切换时替换 ThemeDictionaries 槽位）。</summary>
    public static ResourceDictionary BuildDictionaryFor(GuiThemeVariant variant)
        => BuildDictionary(SchemeFor(variant));

    private static ResourceDictionary BuildDictionary(Scheme scheme) {
        var dict = new ResourceDictionary();
        foreach (var (key, value) in SemanticTuples(scheme))
            dict[key] = ToBrush(value);
        return dict;
    }

    private static IEnumerable<(string Key, string Value)> SemanticTuples(Scheme s) {
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
        yield return ("GuiDangerLevelYellow", s.DangerLevelYellow);
        yield return ("GuiDangerLevelGreen", s.DangerLevelGreen);
        yield return ("GuiDangerLevelRed", s.DangerLevelRed);
        yield return ("GuiDangerLevelBlack", s.DangerLevelBlack);
        yield return ("GuiTurnColorA", s.TurnColorA);
        yield return ("GuiTurnColorB", s.TurnColorB);
    }

    /// <summary>解析十六进制色为不可变画刷（供资源和转换器共用）。</summary>
    public static ISolidColorBrush ToBrush(string hex)
        => new SolidColorBrush(Color.Parse(hex));

    /// <summary>计算两色 WCAG 相对亮度。</summary>
    public static double RelativeLuminance(Color c) {
        double L(double v) => v <= 0.03928
            ? v / 12.92
            : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R / 255.0) + 0.7152 * L(c.G / 255.0) + 0.0722 * L(c.B / 255.0);
    }

    /// <summary>计算两色对比度（1 到 21）。</summary>
    public static double ContrastRatio(Color a, Color b) {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }
}