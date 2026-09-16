namespace JoinCode.Gui.Hosting;

/// <summary>
/// GUI 交互模块 — 注册 Avalonia 专属的交互服务，覆盖 Core 层的 Mock InteractiveService。
/// Order=80 与 CliModule 同级，在 CoreModule(Order=30) 之后注册以覆盖。
/// </summary>
[AppModule(Order = 80)]
public sealed class GuiInteractionModule : IAppModule
{
    /// <summary>模块注册顺序（80 与 CliModule 同级，覆盖 Core 层 Mock 注册）</summary>
    public int Order => 80;

    /// <summary>配置服务</summary>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddSingleton<IInteractiveService, AvaloniaInteractiveService>();
        services.AddSingleton<IWindowShakeService, GuiWindowShakeService>();
    }

    /// <summary>异步配置</summary>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
