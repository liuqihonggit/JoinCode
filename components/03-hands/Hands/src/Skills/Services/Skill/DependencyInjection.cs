
namespace Core.Skills;

public static class SkillsDependencyInjectionExtensions
{
    public static IServiceCollection AddCodeSecurityServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddSkillServices(this IServiceCollection services)
    {
        // ISkillDiscoveryService — [Register] 自动注册（SkillDiscoveryService）
        // SkillDiscoveryOptions — [Register] 自动注册
        // IVariableResolver — [Register] 自动注册（VariableResolver）

        return services;
    }
}
