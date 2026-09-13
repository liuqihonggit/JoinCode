
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册新增服务：VoiceService、VcrService、PreventSleepService、SkillSearchService、
    /// ContextCollapseService、AwaySummaryService、RemotePolicyService、RemoteManagedSettingsService、
    /// IFeatureFlagService、ITeamMemorySyncService、TeamMemorySyncHostedService、McpOAuthService 等。
    /// <para>全部通过 [Register] 特性自动注册，本方法仅作为聚合入口供 <see cref="ServiceRegistration.AddWorkflowServices"/> 调用。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddNewServices(this IServiceCollection services)
    {
        // VoiceService, VcrService, PreventSleepService, SkillSearchService,
        // ContextCollapseService, AwaySummaryService, RemotePolicyService,
        // RemoteManagedSettingsService, IFeatureFlagService (FeatureFlagService),
        // ITeamMemorySyncService (TeamMemorySyncService), TeamMemorySyncHostedService,
        // McpOAuthService — auto-registered via [Register]

        return services;
    }
}
