namespace Core.Bridge;

/// <summary>
/// Bridge 运行管道中间件接口 — 派生自 IMiddleware&lt;BridgeRunContext&gt;
/// </summary>
public interface IBridgeRunMiddleware : IMiddleware<BridgeRunContext> { }
