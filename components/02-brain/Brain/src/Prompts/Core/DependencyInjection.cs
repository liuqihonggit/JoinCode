
namespace Core.Prompts;

public static class PromptsDependencyInjectionExtensions
{
    public static IServiceCollection AddPromptServices(this IServiceCollection services)
    {
        // All registrations migrated to [Register] attribute auto-registration
        return services;
    }
}
