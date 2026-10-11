namespace Core.Bridge;


/// <summary>
/// 桥接远程核心 — v2 初始化管道的静态分部类
/// </summary>
public static partial class BridgeRemoteCore {
    #region initEnvLessBridgeCore — 管道化

    /// <summary>
    /// 初始化 Env-less 桥核心 — 对齐 TS 端 initEnvLessBridgeCore
    /// 通过中间件管道执行，消除 try-catch 样板代码
    /// </summary>
    /// <param name="parameters">v2 桥参数</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="transportFactory">桥接传输工厂</param>
    /// <param name="pipeline">中间件管道</param>
    /// <param name="logger">日志器 — null 表示不记录日志</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>桥接句柄 — 初始化失败时返回 null</returns>
    public static async Task<IReplBridgeHandle?> InitV2BridgeCoreAsync(
        V2BridgeParams parameters,
        HttpClient httpClient,
        IReplBridgeTransportFactory transportFactory,
        MiddlewarePipeline<V2BridgeInitContext> pipeline,
        ILogger? logger = null,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(pipeline);

        var context = new V2BridgeInitContext {
            Parameters = parameters,
            HttpClient = httpClient,
            TransportFactory = transportFactory,
            Logger = logger,
        };

        try {
            await pipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            logger?.LogError(ex, "Bridge: 初始化失败");
            context.Fail(ex.Message);
        }

        return context.Failed ? null : context.Handle;
    }

    #endregion
}