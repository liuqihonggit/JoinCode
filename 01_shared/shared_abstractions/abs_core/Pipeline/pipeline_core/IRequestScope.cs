namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 请求级 Scope — 每个聊天请求/工具调用创建独立 Scope
/// Scoped 服务在 Scope 内单例，Scope 结束时自动释放
/// </summary>
public interface IRequestScope : IAsyncDisposable
{
    /// <summary>
    /// 从当前 Scope 解析服务 — Scoped 服务在 Scope 内单例
    /// </summary>
    T Resolve<T>() where T : notnull;

    /// <summary>
    /// 从当前 Scope 解析可选服务
    /// </summary>
    T? ResolveOptional<T>() where T : class;
}

/// <summary>
/// 请求 Scope 工厂 — 创建请求级 Scope
/// </summary>
public interface IRequestScopeFactory
{
    /// <summary>
    /// 创建请求级 Scope
    /// </summary>
    IRequestScope CreateScope();
}
