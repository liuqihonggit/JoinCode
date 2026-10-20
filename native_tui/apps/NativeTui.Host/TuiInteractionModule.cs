namespace NativeTui.Host;

/// <summary>
/// TUI 交互模块（M7-3）— 注册 NativeTui 宿主专属交互服务：
/// TuiPermissionBridge（shell 绑定桥，构造注入保证引擎容器与宿主同一实例）+
/// TuiPermissionConfirmationHandler（IPermissionConfirmationHandler 引擎确认入口）。
/// Order=80 与 GuiInteractionModule 同级（CoreModule=30 之后覆盖 — 本模块未覆盖项时引擎走自身默认）。
/// </summary>
public sealed class TuiInteractionModule(TuiPermissionBridge bridge) : IAppModule {
    /// <summary>模块注册顺序（80 与 GuiInteractionModule/CliModule 同级）。</summary>
    public int Order => 80;

    /// <summary>交互桥（宿主构造，引擎容器与 Program 共享同一实例）。</summary>
    public TuiPermissionBridge Bridge { get; } = bridge;

    /// <summary>注册交互服务。</summary>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddSingleton(Bridge);
        services.AddSingleton<IPermissionConfirmationHandler, TuiPermissionConfirmationHandler>();
    }

    /// <summary>异步配置（无异步初始化）。</summary>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
