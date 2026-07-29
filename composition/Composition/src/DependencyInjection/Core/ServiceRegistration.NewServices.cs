
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
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
