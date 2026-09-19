namespace JoinCode.App.Modules;

/// <summary>
/// 浏览器模块 — 注册 PuppeteerSharp 浏览器自动化服务
/// </summary>
[AppModule(Order = 60)]
public sealed class BrowserModule : IAppModule {
    /// <summary>模块加载顺序</summary>
    public int Order => 60;

    /// <summary>
    /// 注册浏览器相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddBrowserServices();
    }

    /// <summary>
    /// 异步配置模块（浏览器模块无需额外配置）
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}