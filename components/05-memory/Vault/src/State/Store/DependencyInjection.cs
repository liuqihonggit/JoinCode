namespace State;

public static class StateDependencyInjectionExtensions
{
    public static IServiceCollection AddVaultStateServices(this IServiceCollection services)
    {
        // IStateService — 根据 JCC_STATE_MODE 环境变量决定后端
        // 默认 File（SQLite 持久化），InMemory=纯内存0磁盘IO（调试/E2E测试用）
        // 注意: [Register] 自动注册的 StateService 已在此处被覆盖（后注册 wins）
        var stateMode = System.Environment.GetEnvironmentVariable(JccEnvVar.StateMode.ToValue());
        if (string.Equals(stateMode, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IStateService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IStateService (InMemory)");
                var svc = new InMemoryStateService(sp.GetRequiredService<IClockService>());
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IStateService (InMemory)");
                return svc;
            });
        }

        return services;
    }
}
