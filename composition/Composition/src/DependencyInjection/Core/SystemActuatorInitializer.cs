namespace Core.DependencyInjection;

/// <summary>
/// 系统执行器初始化器 — 应用启动时调用一次
/// 检测所有执行器能力并注册工厂到 SystemActuatorRegistry
/// 替代原 CapabilityInitializer
/// </summary>
public static class SystemActuatorInitializer
{
    private static int _initialized;

    /// <summary>
    /// 初始化系统执行器 — 在 DI 容器构建完成后调用
    /// </summary>
    public static void Initialize(IFileSystem fs, ILogger? logger = null)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1) return;

        BashSystemActuator.CreateCapability(fs, logger);
        PowerShellSystemActuator.CreateCapability(fs, logger);
        CmdSystemActuator.CreateCapability(fs, logger);
        PythonSystemActuator.CreateCapability(fs, logger);

        var factories = new Dictionary<SystemActuatorKind, Func<RegistryDeps, ISystemActuator>>
        {
            [SystemActuatorKind.Bash] = deps => new BashSystemActuator(
                deps.FileSystem, logger: deps.Logger, sandboxManager: deps.SandboxManager,
                preventSleepService: deps.PreventSleepService, config: deps.Config),
            [SystemActuatorKind.PowerShell] = deps => new PowerShellSystemActuator(
                deps.FileSystem, logger: deps.Logger, sandboxManager: deps.SandboxManager,
                preventSleepService: deps.PreventSleepService, config: deps.Config),
            [SystemActuatorKind.Cmd] = deps => new CmdSystemActuator(
                deps.FileSystem, logger: deps.Logger, sandboxManager: deps.SandboxManager,
                preventSleepService: deps.PreventSleepService, config: deps.Config),
            [SystemActuatorKind.Python] = deps => new PythonSystemActuator(
                deps.FileSystem, logger: deps.Logger, sandboxManager: deps.SandboxManager,
                preventSleepService: deps.PreventSleepService, config: deps.Config),
        };

        SystemActuatorRegistry.RegisterFactories(factories);

        logger?.LogInformation("SystemActuatorRegistry initialized: {Kinds}",
            string.Join(", ", factories.Keys.Select(k => k.Id)));
    }

    /// <summary>
    /// 重置 — 仅用于测试
    /// </summary>
    internal static void Reset()
    {
        _initialized = 0;
        SystemActuatorBase.ResetCapabilityCache();
    }
}
