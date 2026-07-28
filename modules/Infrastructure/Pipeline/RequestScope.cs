namespace Infrastructure.Pipeline;

/// <summary>
/// 请求级 Scope 实现 — 基于 Microsoft.Extensions.DependencyInjection
/// </summary>
public sealed class RequestScope : IRequestScope
{
    private readonly IServiceScope _scope;

    public RequestScope(IServiceScope scope)
    {
        _scope = scope;
    }

    public T Resolve<T>() where T : notnull
        => _scope.ServiceProvider.GetRequiredService<T>();

    public T? ResolveOptional<T>() where T : class
        => _scope.ServiceProvider.GetService<T>();

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 请求 Scope 工厂实现
/// </summary>
[Register(typeof(IRequestScopeFactory))]
public sealed partial class RequestScopeFactory : IRequestScopeFactory
{
    [Inject] private readonly IServiceScopeFactory _scopeFactory;

    public IRequestScope CreateScope()
        => new RequestScope(_scopeFactory.CreateScope());
}
