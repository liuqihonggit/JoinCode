namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限检查拦截器接口 — 工具调用前的权限验证
/// </summary>
public interface IPermissionCheckingInterceptor : IDisposable
{
    /// <summary>
    /// 检查权限并在被拒绝时抛出异常
    /// </summary>
    Task CheckPermissionOrThrowAsync(ToolInvokeContext context, CancellationToken cancellationToken = default);
}
