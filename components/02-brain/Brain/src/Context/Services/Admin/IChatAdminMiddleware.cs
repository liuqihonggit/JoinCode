namespace Core.Context;

/// <summary>
/// 管理操作中间件 — 拦截和转换管理操作（清空历史、压缩、撤回等）
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IChatAdminMiddleware : IMiddleware<ChatAdminContext> { }
