namespace JoinCode.ChatCommands;

/// <summary>
/// 轻量级 IServiceProvider — 优先返回 CommandServices，其余转发到 fallback provider
/// 用于 ChatCommandContext.Services (IServiceProvider)，避免修改主 DI 容器
/// </summary>
public sealed class CommandServiceProvider : IServiceProvider
{
    private readonly CommandServices _commandServices;
    private readonly IServiceProvider? _fallback;

    /// <summary>
    /// 构造 — commandServices 为强类型服务包，fallback 为可选的主 DI 容器（解析 CommandServices 之外的服务）
    /// 未指定 fallback 时自动使用 commandServices.ServiceProvider
    /// </summary>
    public CommandServiceProvider(CommandServices commandServices, IServiceProvider? fallback = null)
    {
        _commandServices = commandServices;
        _fallback = fallback ?? commandServices.ServiceProvider;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(CommandServices))
            return _commandServices;
        return _fallback?.GetService(serviceType);
    }
}
