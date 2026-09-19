namespace JoinCode.App.Modules;

/// <summary>
/// 管道模块 — 注册命名管道通信服务
/// </summary>
[AppModule(Order = 70)]
public sealed class PipeModule : IAppModule {
    /// <summary>模块加载顺序，数值越小越先加载</summary>
    public int Order => 70;

    /// <summary>
    /// 注册命名管道通信相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.RegisterPipeServices();
    }

    /// <summary>
    /// 异步配置模块，管道模块无需异步初始化
    /// </summary>
    /// <param name="services">已构建的服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}