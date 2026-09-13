namespace Infrastructure.Pipeline;

/// <summary>
/// 请求级 Scope 实现 — 基于 Microsoft.Extensions.DependencyInjection
/// </summary>
public sealed class RequestScope : IRequestScope
{
    private readonly IServiceScope _scope;

    /// <summary>
    /// 构造请求级 Scope
    /// </summary>
    /// <param name="scope">DI 服务范围</param>
    public RequestScope(IServiceScope scope)
    {
        _scope = scope;
    }

    /// <inheritdoc/>
    public T Resolve<T>() where T : notnull
        => _scope.ServiceProvider.GetRequiredService<T>();

    /// <inheritdoc/>
    public T? ResolveOptional<T>() where T : class
        => _scope.ServiceProvider.GetService<T>();

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 请求 Scope 工厂实现
/// </summary>
[Register(typeof(IRequestScopeFactory), ServiceLifetime.Singleton)]
public sealed partial class RequestScopeFactory : ServiceEntity, IRequestScopeFactory
{
    /// <summary>
    /// 构造请求 Scope 工厂
    /// </summary>
    /// <param name="scopeFactory">DI 服务范围工厂</param>
    public RequestScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    private readonly IServiceScopeFactory _scopeFactory;

    /// <inheritdoc/>
    public IRequestScope CreateScope()
        => new RequestScope(_scopeFactory.CreateScope());
}
