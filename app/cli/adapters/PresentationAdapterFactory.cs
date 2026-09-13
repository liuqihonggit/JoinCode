namespace JoinCode.Adapters;

/// <summary>
/// 表示层适配器工厂 — 仅 CLI 模式
/// </summary>
public sealed class PresentationAdapterFactory : IPresentationAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PresentationAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IPresentationAdapter Create(PresentationMode mode)
    {
        return new ConsolePresentationAdapter(
            _serviceProvider.GetRequiredService<IConsoleOutput>());
    }

    public IPresentationAdapter CreateForCurrentEnvironment()
    {
        return Create(PresentationMode.Cli);
    }
}
