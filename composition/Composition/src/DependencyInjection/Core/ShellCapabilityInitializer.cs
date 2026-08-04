namespace Core.DependencyInjection;

/// <summary>
/// Shell 能力缓存初始化器 — 应用启动时调用一次
/// 检测所有 Shell 类型并填充 ShellCapabilityCache + ShellProviderFactory
/// </summary>
public static class ShellCapabilityInitializer
{
    /// <summary>
    /// 初始化 Shell 能力缓存 — 在 DI 容器构建完成后调用
    /// </summary>
    public static void Initialize(IFileSystem fs, ILogger? logger = null)
    {
        if (ShellCapabilityCache.IsInitialized)
            return;

        var bashProvider = new BashCapabilityProvider(fs, logger: logger as ILogger<BashCapabilityProvider>);
        var psProvider = new PowerShellCapabilityProvider();
        var pythonProvider = new PythonCapabilityProvider();

        var capabilities = new Dictionary<ShellType, ShellCapability>
        {
            [ShellType.Bash] = bashProvider.GetCapability(fs, logger),
            [ShellType.PowerShell] = psProvider.GetCapability(fs, logger),
            [ShellType.Python] = pythonProvider.GetCapability(fs, logger),
        };

        ShellCapabilityCache.Initialize(capabilities);

        var factories = new Dictionary<ShellType, Func<ShellCapability, IFileSystem, ILogger?, ShellProviderBase>>
        {
            [ShellType.Bash] = (cap, fs, log) => bashProvider.CreateProvider(cap, fs, log),
            [ShellType.PowerShell] = (cap, fs, log) => psProvider.CreateProvider(cap, fs, log),
            [ShellType.Python] = (cap, fs, log) => pythonProvider.CreateProvider(cap, fs, log),
        };

        ShellProviderFactory.Register(factories);

        logger?.LogInformation("ShellCapabilityCache initialized: {Types}", string.Join(", ", capabilities.Keys));
    }
}
