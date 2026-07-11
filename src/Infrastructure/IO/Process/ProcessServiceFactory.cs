namespace IO.ProcessService;

/// <summary>
/// 进程服务工厂 — 根据 JCC_PROCESS_MODE 环境变量创建对应的 IProcessService 实例
/// 用于 DI 容器构建之前的场景，DI 容器构建后应通过注入 IProcessService 使用
/// </summary>
public static class ProcessServiceFactory
{
    /// <summary>
    /// 根据环境变量创建 IProcessService 实例。
    /// JCC_PROCESS_MODE=NoOp → NoOpProcessService（跳过所有进程操作）
    /// 其他/未设置 → PhysicalProcessService（真实进程，默认）
    /// </summary>
    public static IProcessService Create()
    {
        var mode = EnvHelper.Get(JccEnvVar.ProcessMode);
        if (string.Equals(mode, "NoOp", StringComparison.OrdinalIgnoreCase))
            return new NoOpProcessService();
        return new PhysicalProcessService();
    }
}
