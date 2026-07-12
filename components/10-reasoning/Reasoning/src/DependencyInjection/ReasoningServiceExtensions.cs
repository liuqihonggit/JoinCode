namespace JoinCode.Reasoning.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 推理引擎DI注册
/// </summary>
public static class ReasoningServiceExtensions
{
    /// <summary>
    /// 注册推理引擎（吃熊猫罪预设）
    /// </summary>
    public static IServiceCollection AddReasoning(this IServiceCollection services, ReasoningOptions? options = null)
    {
        var opts = options ?? ReasoningOptions.Panda;
        services.AddSingleton(opts);
        services.AddSingleton<IReasoningEngine, ReasoningEngine>();
        services.AddSingleton<IReasoningAgent, ProsecutorAgent>();
        services.AddSingleton<IReasoningAgent, DefenderAgent>();
        services.AddSingleton<IReasoningAgent, JudgeAgent>();
        return services;
    }

    /// <summary>
    /// 注册推理引擎（指定预设）
    /// </summary>
    public static IServiceCollection AddReasoning(this IServiceCollection services, ReasoningPreset preset)
    {
        return services.AddReasoning(ReasoningOptions.FromPreset(preset));
    }
}
