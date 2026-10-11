namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 主题管理 partial — 从 settings.json 加载主题、外部变更热重载、多主题循环切换。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>从 settings.json 异步加载主题并应用到 CurrentTheme（启动 / 引擎热切换后调用）</summary>
    private void LoadThemeFromSettings() {
        _ = Task.Run(async () => {
            try {
                var theme = await _session.GetThemeAsync().WaitAsync(Timeout);
                // Auto 保持默认 CurrentTheme（GUI 无 Auto 选项，避免按时间覆盖用户上次明确选择）
                if (theme is ThemeKind.Auto)
                    return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    if (_hasGuiThemePreference) return;
                    using var _ = _gate.EnterApplyingThemeScope();
                    CurrentTheme = ThemeConverter.ToVariant(theme);
                });
            } catch (Exception ex) {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
    }

    /// <summary>settings.json theme 外部变更事件处理 — 驱动 GUI 热重载（双向绑定）</summary>
    private void OnThemeChanged(object? sender, ThemeKind theme) {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (ThemeConverter.FromVariant(CurrentTheme) == theme) return;
            using var _ = _gate.EnterApplyingThemeScope();
            CurrentTheme = ThemeConverter.ToVariant(theme);
        });
    }

    /// <summary>循环切换主题：Dark → Light → SolarizedDark → SolarizedLight → Dark</summary>
    [RelayCommand]
    private void ToggleTheme() => CurrentTheme = CurrentTheme switch {
        GuiPalette.GuiThemeVariant.Dark => GuiPalette.GuiThemeVariant.Light,
        GuiPalette.GuiThemeVariant.Light => GuiPalette.GuiThemeVariant.SolarizedDark,
        GuiPalette.GuiThemeVariant.SolarizedDark => GuiPalette.GuiThemeVariant.SolarizedLight,
        _ => GuiPalette.GuiThemeVariant.Dark
    };

    partial void OnCurrentThemeChanged(GuiPalette.GuiThemeVariant value) {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
        OnPropertyChanged(nameof(SelectedThemeOption));
    }
}
