
namespace JoinCode.Pipe;

/// <summary>
/// 管道路由注册器接口 — 将一组路由注册到桥接服务器
/// </summary>
public interface IPipeRouteRegistrar {
    /// <summary>
    /// 将本注册器负责的路由注册到指定的桥接服务器
    /// </summary>
    /// <param name="server">接收路由注册的桥接服务器</param>
    void RegisterRoutes(Core.Bridge.BridgeServer server);
}