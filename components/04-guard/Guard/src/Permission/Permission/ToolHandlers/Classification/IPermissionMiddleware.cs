
namespace Core.Permission;

/// <summary>
/// 权限检查中间件接口 — 继承通用中间件契约，用于权限检查管道
/// </summary>
public interface IPermissionMiddleware : IMiddleware<PermissionCheckContext> { }
