namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 主题管理 partial — 从 settings.json 加载主题、外部变更热重载、切换深浅。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>从 settings.json 异步加载主题并应用到 IsDarkTheme（启动 / 引擎热切换后调用）</summary>
    private void LoadThemeFromSettings()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var theme = await _session.GetThemeAsync().WaitAsync(Timeout);
                // Auto 保持默认 IsDarkTheme（GUI 无 Auto 选项，避免按时间覆盖用户上次明确选择）
                if (theme is ThemeKind.Auto)
                    return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    using var _ = _gate.EnterApplyingThemeScope();
                    IsDarkTheme = ThemeConverter.ToIsDark(theme);
                });
            }
            catch (Exception ex)
            {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
    }

    /// <summary>settings.json theme 外部变更事件处理 — 驱动 GUI 热重载（双向绑定）</summary>
    private void OnThemeChanged(object? sender, ThemeKind theme)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            using var _ = _gate.EnterApplyingThemeScope();
            IsDarkTheme = ThemeConverter.ToIsDark(theme);
        });
    }

    /// <summary>切换深浅主题（占位阶段仅记录状态，UI 由 View 层响应）</summary>
    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;
}
