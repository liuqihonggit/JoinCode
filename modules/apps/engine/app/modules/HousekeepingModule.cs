namespace JoinCode.App.Modules;

/// <summary>
/// 家政清理模块 — 注册后台家政清理 HostedService + EntityReaper 实体回收
/// 对齐 TS startBackgroundHousekeeping: 延迟10分钟+24小时循环清理
/// EntityReaper: 延迟30秒+60秒循环扫描可回收/超时/泄漏 Entity
/// </summary>
[AppModule(Order = 75)]
public sealed class HousekeepingModule : IAppModule {
    /// <summary>模块加载顺序，值越大越靠后</summary>
    public int Order => 75;

    /// <summary>注册家政清理与实体回收 HostedService</summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddHostedService<Infrastructure.Housekeeping.BackgroundHousekeepingService>(sp => {
            var housekeeping = sp.GetRequiredService<IHousekeepingService>();
            var fs = sp.GetRequiredService<IFileSystem>();
            var clock = sp.GetRequiredService<IClockService>();
            var logger = sp.GetService<ILogger<Infrastructure.Housekeeping.BackgroundHousekeepingService>>();
            return new Infrastructure.Housekeeping.BackgroundHousekeepingService(housekeeping, fs, clock, logger);
        });

        services.AddHostedService<Infrastructure.EntityReaper.BackgroundEntityReaperService>(sp => {
            var reaper = sp.GetRequiredService<IEntityReaper>();
            var clock = sp.GetRequiredService<IClockService>();
            var logger = sp.GetService<ILogger<Infrastructure.EntityReaper.BackgroundEntityReaperService>>();
            return new Infrastructure.EntityReaper.BackgroundEntityReaperService(reaper, clock, null, logger);
        });
    }

    /// <summary>异步配置模块；本模块无异步配置工作，直接返回已完成任务</summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}