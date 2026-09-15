namespace JoinCode.Gui.Hosting;

/// <summary>
/// GUI 模式窗口震动服务 — 通过 <see cref="ShakeAnimationHelper"/> 震动 Avalonia MainWindow。
/// 在 <see cref="GuiInteractionModule"/> 中显式注册，覆盖 CLI 模式的 <c>Win32WindowShakeService</c>。
/// </summary>
public sealed class GuiWindowShakeService : IWindowShakeService
{
    private readonly ILogger<GuiWindowShakeService>? _logger;

    /// <summary>
    /// 构造 GUI 窗口震动服务。
    /// </summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public GuiWindowShakeService(ILogger<GuiWindowShakeService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 震动 GUI 主窗口 — 通过 <see cref="ShakeAnimationHelper.ShakeWindow"/> 施加 X 轴阻尼动画。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ShakeWindowAsync(CancellationToken cancellationToken = default)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            _logger?.LogWarning("未找到主窗口，无法震动");
            return Task.CompletedTask;
        }

        ShakeAnimationHelper.ShakeWindow(window, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 闪烁任务栏 — GUI 模式下也用窗口震动代替（GUI 进程无控制台窗口句柄）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task FlashTaskbarAsync(CancellationToken cancellationToken = default)
        => ShakeWindowAsync(cancellationToken);

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
