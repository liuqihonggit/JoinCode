namespace JoinCode.Dream.Pipeline;

/// <summary>
/// Dream 中间件接口 — 处理 DreamContext 的管道中间件契约
/// </summary>
public interface IDreamMiddleware : IMiddleware<DreamContext> { }