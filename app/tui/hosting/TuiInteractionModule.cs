namespace JoinCode.Tui.Hosting;

/// <summary>
/// TUI 交互模块 — 注册 Terminal.Gui 专属交互服务，覆盖 Core 层的 Mock InteractiveService。
/// Order=80 与 CliModule/GuiInteractionModule 同级，在 CoreModule(Order=30) 之后注册以覆盖。
/// 服务实例由 TuiModeRunner 启动时 Attach(painter, dialogView) 绑定真实 UI 通道。
/// </summary>
[AppModule(Order = 80)]
public sealed class TuiInteractionModule : IAppModule
{
    /// <summary>模块加载顺序（80 = TUI 交互层）</summary>
    public int Order => 80;

    /// <summary>注册 TUI 交互服务到 DI 容器</summary>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddSingleton<TerminalGuiInteractiveService>();
        services.AddSingleton<IInteractiveService>(sp => sp.GetRequiredService<TerminalGuiInteractiveService>());
    }

    /// <summary>异步配置（无操作，返回已完成任务）</summary>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
