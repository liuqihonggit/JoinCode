namespace JoinCode.App.Modules;


/// <summary>
/// 时钟模块 — 注册定时任务相关服务
/// </summary>
[AppModule(Order = 40)]
public sealed class ClockModule : IAppModule
{
    /// <summary>模块加载顺序 — 40，在基础服务之后加载</summary>
    public int Order => 40;

    /// <summary>注册时钟相关服务</summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddClockServices();
    }

    /// <summary>异步配置 — 执行目标引擎的后置配置</summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步配置操作的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
    {
        var postConfigure = services.GetService<IGoalEnginePostConfigure>();
        postConfigure?.Configure();
        return Task.CompletedTask;
    }
}
