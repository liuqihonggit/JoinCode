
namespace JoinCode.Dream;

/// <summary>
/// Dream 插件服务注册扩展
/// </summary>
public static class DreamPluginServiceExtensions
{
    /// <summary>
    /// 添加 Dream 插件服务
    /// </summary>
    public static IServiceCollection AddDreamPluginServices(this IServiceCollection services)
    {
        // 注册配置（AutoDreamConfig 无 [Register]，需手动注册）
        services.AddSingleton<AutoDreamConfig>(sp =>
        {
            var config = AutoDreamConfigBuilder.Create()
                .WithMinSessions(2)
                .Build();
            return config;
        });

        // 以下服务已通过 [Register] 特性自动注册，无需手动注册：
        // IChatCompletionClient → ChatCompletionClient
        // ISessionScanner → DefaultSessionScanner
        // IDreamTaskPersistence → JsonFileDreamTaskPersistence
        // IDreamTaskRegistry → PersistentDreamTaskRegistry
        // IDreamFeature → DreamFeature
        // IWorkflowPlugin → DreamPlugin
        // ICommandRegistrationHook → DreamPlugin（多接口注册）

        return services;
    }
}
