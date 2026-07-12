namespace JoinCode.Reasoning.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 推理引擎DI注册
/// </summary>
public static class ReasoningServiceExtensions
{
    public static IServiceCollection AddReasoning(this IServiceCollection services)
    {
        services.AddSingleton<IReasoningEngine, ReasoningEngine>();
        services.AddSingleton<IReasoningAgent, ProsecutorAgent>();
        services.AddSingleton<IReasoningAgent, DefenderAgent>();
        services.AddSingleton<IReasoningAgent, JudgeAgent>();
        return services;
    }
}
