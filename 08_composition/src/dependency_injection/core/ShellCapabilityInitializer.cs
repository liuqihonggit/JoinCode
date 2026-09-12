namespace Core.DependencyInjection;

/// <summary>
/// Shell 能力缓存初始化器 — 已迁移至 SystemActuatorInitializer
/// 保留此类型作为转发入口，内部委托给 SystemActuatorInitializer.Initialize
/// </summary>
public static class ShellCapabilityInitializer
{
    /// <summary>
    /// 初始化 Shell 能力缓存 — 转发到 SystemActuatorInitializer.Initialize
    /// 在 DI 容器构建完成后调用
    /// </summary>
    public static void Initialize(IFileSystem fs, ILogger? logger = null)
    {
        SystemActuatorInitializer.Initialize(fs, logger);
    }
}
