namespace JoinCode.Gui.Hosting;

/// <summary>
/// GUI 交互模块 — 注册 Avalonia 专属的交互服务，覆盖 Core 层的 Mock InteractiveService。
/// 在 gui profile 中排于 CoreModule 之后以覆盖 Mock 注册。
/// </summary>
[AppModule]
public sealed class GuiInteractionModule : IAppModule {
    /// <summary>配置服务</summary>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddSingleton<IInteractiveService, AvaloniaInteractiveService>();
        services.AddSingleton<IWindowShakeService, GuiWindowShakeService>();
        // 权限确认器 — 引擎确认点转接宿主异步委托（WebUI broker / Avalonia 对话框），未注入委托时 fail-closed 拒绝
        services.AddSingleton<IPermissionConfirmationHandler, GuiPermissionConfirmationHandler>();
    }

    /// <summary>异步配置</summary>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}