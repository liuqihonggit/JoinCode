namespace JoinCode.Adapters;

/// <summary>
/// 表示层适配器工厂 — 仅 CLI 模式
/// </summary>
public sealed class PresentationAdapterFactory : IPresentationAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 构造表示层适配器工厂
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供者，用于解析表示层所需依赖</param>
    public PresentationAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 按指定表示模式创建适配器
    /// </summary>
    /// <param name="mode">表示层模式</param>
    /// <returns>对应模式的表示层适配器实例</returns>
    public IPresentationAdapter Create(PresentationMode mode)
    {
        return new ConsolePresentationAdapter(
            _serviceProvider.GetRequiredService<IConsoleOutput>());
    }

    /// <summary>
    /// 根据当前运行环境自动选择并创建适配器
    /// </summary>
    /// <returns>适配当前环境的表示层适配器实例</returns>
    public IPresentationAdapter CreateForCurrentEnvironment()
    {
        return Create(PresentationMode.Cli);
    }
}
