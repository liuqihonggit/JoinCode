namespace Services.Shell.Providers;

/// <summary>
/// Shell 执行器工厂 — 从 ShellCapabilityCache 获取能力描述，创建短命 ShellProviderBase 实例
/// 替代 DI 注入 ShellCapabilityProvider，每次命令执行时调用
/// </summary>
public static class ShellProviderFactory
{
    private static FrozenDictionary<ShellType, Func<ShellCapability, IFileSystem, ILogger?, ShellProviderBase>>? _factories;

    /// <summary>
    /// 注册工厂方法 — 应用启动时调用一次
    /// </summary>
    public static void Register(
        IReadOnlyDictionary<ShellType, Func<ShellCapability, IFileSystem, ILogger?, ShellProviderBase>> factories)
    {
        _factories = factories.ToFrozenDictionary();
    }

    /// <summary>
    /// 创建短命 Shell 执行器 — 每次命令执行时调用
    /// </summary>
    public static ShellProviderBase Create(ShellType type, IFileSystem fs, ILogger? logger = null)
    {
        if (_factories is null)
            throw new InvalidOperationException("ShellProviderFactory not initialized. Call Register() first.");

        if (!_factories.TryGetValue(type, out var factory))
            throw new InvalidOperationException($"No ShellProviderFactory registered for {type}");

        var capability = ShellCapabilityCache.Get(type);
        return factory(capability, fs, logger);
    }

    /// <summary>
    /// 重置 — 仅用于测试
    /// </summary>
    internal static void Reset()
    {
        _factories = null;
    }
}
