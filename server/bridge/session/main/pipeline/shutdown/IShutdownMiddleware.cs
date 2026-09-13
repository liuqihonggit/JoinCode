namespace Core.Bridge;


/// <summary>
/// 关闭中间件接口 — 桥接关闭管道的中间件契约
/// </summary>
public interface IShutdownMiddleware : IMiddleware<ShutdownContext> { }
