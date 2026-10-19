namespace JoinCode.App.Modules;

/// <summary>
/// CLI 自动注册兜底模块 — 注册 cli 程序集内 49 个 [Register] 类型（ADR 0143 Step 3b 从 CoreModule 拆出）。
/// <para>telemetry hooks/ConfirmationGate/OnboardingFlowController/VimEngine 等宿主侧资产由生成器方法兜底；</para>
/// <para>gui/tui 会话经 extraModules 传入本模块，保持 CoreModule 拆分后行为零变化。</para>
/// </summary>
[AppModule(Order = 31)]
public sealed class CliRegistrationModule : IAppModule {
    /// <summary>模块加载顺序 — 31，紧随引擎 CoreModule(30) 之后</summary>
    public int Order => 31;

    /// <summary>
    /// 注册 cli 程序集自动注册服务 — 源码生成器按程序集名生成方法（"jcc"→"Jcc"）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddJccAutoRegisteredServices();
        services.AddJccAutoRegisteredOptions();
    }

    /// <summary>
    /// 异步配置 — 本模块无异步初始化，直接返回完成
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
