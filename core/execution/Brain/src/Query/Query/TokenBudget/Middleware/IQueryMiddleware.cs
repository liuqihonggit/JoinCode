
namespace Core.Query;

/// <summary>
/// 查询中间件接口 — 扩展通用 Task 中间件，用于 QueryEngine 管道
/// </summary>
public interface IQueryMiddleware : IMiddleware<QueryMiddlewareContext> { }
