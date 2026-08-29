namespace JoinCode.Tui.Hosting;

/// <summary>
/// TUI 交互模块 — 注册 Terminal.Gui 专属交互服务，覆盖 Core 层的 Mock InteractiveService。
/// Order=80 与 CliModule/GuiInteractionModule 同级，在 CoreModule(Order=30) 之后注册以覆盖。
/// 服务实例由 TuiModeRunner 启动时 Attach(painter, dialogView) 绑定真实 UI 通道。
/// </summary>
[AppModule(Order = 80)]
public sealed class TuiInteractionModule : IAppModule
{
    public int Order => 80;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddSingleton<TerminalGuiInteractiveService>();
        services.AddSingleton<IInteractiveService>(sp => sp.GetRequiredService<TerminalGuiInteractiveService>());
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
