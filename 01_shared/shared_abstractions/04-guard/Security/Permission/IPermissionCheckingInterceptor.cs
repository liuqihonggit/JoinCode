namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限检查拦截器接口 — 工具调用前的权限验证
/// </summary>
public interface IPermissionCheckingInterceptor : IDisposable
{
    /// <summary>
    /// 检查权限并返回决策结果 — 中间件管道用返回值传递权限决策,不抛异常
    /// </summary>
    Task<PermissionCheckOutcome> CheckPermissionAsync(ToolInvokeContext context, CancellationToken cancellationToken = default);
}
