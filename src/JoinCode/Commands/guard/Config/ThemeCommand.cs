namespace JoinCode.ChatCommands;

/// <summary>
/// /theme 命令 — 对齐 TS ThemePicker.tsx
/// TS 是交互式主题选择器（React Select），C# 是命令行参数切换
/// 对齐内容：7个主题选项(auto/dark/light/dark-daltonized/light-daltonized/dark-ansi/light-ansi)
/// 架构差异：TS 有实时预览+语法高亮切换，C# 是直接切换
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Theme, Description = "切换控制台主题", Usage = "/theme [dark|light|auto|dark-daltonized|light-daltonized|dark-ansi|light-ansi]", Category = ChatCommandCategory.Config)]
public sealed class ThemeCommand : ChatCommandBase
{
    /// <summary>
    /// 主题显示标签映射 — 仅 UI 显示字符串,不参与序列化
    /// </summary>
    private static readonly FrozenDictionary<ThemeKind, string> ThemeLabels = BuildThemeLabels();

    private static FrozenDictionary<ThemeKind, string> BuildThemeLabels()
    {
        return new Dictionary<ThemeKind, string>
        {
            [ThemeKind.Auto] = L.T(StringKey.ThemeLabelAuto),
            [ThemeKind.Dark] = L.T(StringKey.ThemeLabelDark),
            [ThemeKind.Light] = L.T(StringKey.ThemeLabelLight),
            [ThemeKind.DarkDaltonized] = L.T(StringKey.ThemeLabelDarkDaltonized),
            [ThemeKind.LightDaltonized] = L.T(StringKey.ThemeLabelLightDaltonized),
            [ThemeKind.DarkAnsi] = L.T(StringKey.ThemeLabelDarkAnsi),
            [ThemeKind.LightAnsi] = L.T(StringKey.ThemeLabelLightAnsi),
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// 主题选项 — 从 ThemeKind 枚举派生(单一数据源),对应对齐 TS ThemePicker.tsx themeOptions
    /// </summary>
    private static readonly (string Value, string Label)[] ThemeOptions = BuildThemeOptions();

    /// <summary>
    /// 从 ThemeKind 枚举构建主题选项数组 — 单一数据源
    /// </summary>
    private static (string Value, string Label)[] BuildThemeOptions()
    {
        return Enum.GetValues<ThemeKind>()
            .Select(kind => (Value: kind.ToValue(), Label: ThemeLabels[kind]))
            .ToArray();
    }

    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var theme = args.Length > 0 ? args[0].ToLowerInvariant() : "show";

        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));

        // 查找匹配的主题 — 使用 FromValue 解析
        var matchedKind = ThemeKindExtensions.FromValue(theme);
        if (matchedKind is not null && ThemeLabels.TryGetValue(matchedKind.Value, out var label))
        {
            var themeValue = matchedKind.Value.ToValue();
            await SetThemeAsync(configService, themeValue, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.ThemeSetTo), label)}{AnsiStyleConstants.Reset}");
        }
        else if (theme == "show")
        {
            await ShowThemePickerAsync(configService, context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.ThemeSetUnknown), theme));
            TerminalHelper.NewLine();
            await ShowThemePickerAsync(configService, context.CancellationToken).ConfigureAwait(false);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 显示主题选择器(交互式)
    /// 对齐 TS: ThemePicker — 上下键选择主题+Enter切换+Esc取消
    /// </summary>
    private async Task ShowThemePickerAsync(IConfigurationService? configService, CancellationToken ct)
    {
        var currentTheme = await GetCurrentThemeAsync(configService).ConfigureAwait(false);

        // 交互模式:使用 Selector 组件
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var selector = new Selector<(string Value, string Label)>(
                L.T(StringKey.ThemeSelectorTitle),
                [.. ThemeOptions],
                t => t.Label + (t.Value == currentTheme ? " *" : ""),
                t => t.Value,
                enableSearch: false);

            var result = await selector.ShowAsync(ct).ConfigureAwait(false);

            if (result.Cancelled || result.Selected.Equals(default))
            {
                TerminalHelper.WriteLine(L.T(StringKey.ThemeCancelled));
                return;
            }

            await SetThemeAsync(configService, result.Selected.Value, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.ThemeSetTo), result.Selected.Label)}{AnsiStyleConstants.Reset}");
            return;
        }

        // 非交互模式回退:纯文本列表
        var currentKind = ThemeKindExtensions.FromValue(currentTheme);
        var currentLabel = currentKind is not null && ThemeLabels.TryGetValue(currentKind.Value, out var l)
            ? l
            : currentTheme;

        TerminalHelper.WriteLine(string.Format(L.T(StringKey.ThemeCurrentLabel), currentLabel));
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.ThemeAvailableHeader));

        foreach (var (value, label) in ThemeOptions)
        {
            var marker = value == currentTheme ? " *" : "";
            TerminalHelper.WriteLine($"  {value,-20} {label}{marker}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.ThemeUsageHint)}{AnsiStyleConstants.Reset}");
    }

    private async Task SetThemeAsync(IConfigurationService? configService, string theme, CancellationToken ct)
    {
        if (configService is not null)
        {
            await configService.SetAsync(ConfigKeyConstants.Theme, theme, ct).ConfigureAwait(false);
        }

        ApplyTheme(theme);
    }

    private static async Task<string> GetCurrentThemeAsync(IConfigurationService? configService)
    {
        if (configService is not null)
        {
            var theme = await configService.GetAsync(ConfigKeyConstants.Theme).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(theme)) return theme;
        }

        return ThemeKindConstants.Auto;
    }

    private void ApplyTheme(string theme)
    {
        // 解析 auto 主题
        var resolvedTheme = theme;
        if (theme == ThemeKindConstants.Auto)
        {
            var hour = DateTime.Now.Hour;
            resolvedTheme = hour >= 6 && hour < 18
                ? ThemeKindConstants.Light
                : ThemeKindConstants.Dark;
        }

        // 解析 daltonized → 基础主题 + 色盲友好调色
        // 解析 ansi → 基础主题 + 仅 ANSI 颜色
        // 待办: 实现色盲友好调色板和 ANSI-only 颜色映射
        var isDaltonized = theme.Contains("daltonized");
        var isAnsi = theme.Contains("ansi");
        var isDark = resolvedTheme.Contains("dark") || (!resolvedTheme.Contains("light") && theme.Contains("dark"));

        if (isDark)
        {
            TerminalHelper.BackgroundColor = ConsoleColor.Black;
            TerminalHelper.ForegroundColor = isDaltonized ? ConsoleColor.Cyan : ConsoleColor.White;
        }
        else
        {
            TerminalHelper.BackgroundColor = ConsoleColor.White;
            TerminalHelper.ForegroundColor = isDaltonized ? ConsoleColor.DarkCyan : ConsoleColor.Black;
        }

        // 仅在输出未重定向时执行 Console.Clear()
        if (!TerminalHelper.IsOutputRedirected)
        {
            TerminalHelper.ClearScreen();
        }
    }
}
