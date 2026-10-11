namespace Core.Bridge;


/// <summary>
/// HandleWork 中间件接口 — 处理 HandleWorkContext 的管道中间件契约
/// </summary>
public interface IHandleWorkMiddleware : IMiddleware<HandleWorkContext> { }