namespace Core.Bridge;


public static partial class BridgeRemoteCore
{
    #region initBridgeCore (v1 env-based) — 管道化

    /// <summary>
    /// 初始化 v1 env-based 桥核心 — 对齐 TS 端 initBridgeCore
    /// 通过中间件管道执行，消除 try-catch 样板代码
    /// </summary>
    /// <param name="parameters">桥核心参数</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="transportFactory">桥传输工厂</param>
    /// <param name="pipeline">中间件管道</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>桥句柄，失败返回 null</returns>
    public static async Task<IReplBridgeHandle?> InitBridgeCoreAsync(
        BridgeCoreParams parameters,
        HttpClient httpClient,
        IFileSystem fs,
        IReplBridgeTransportFactory transportFactory,
        MiddlewarePipeline<V1BridgeInitContext> pipeline,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(pipeline);

        var context = new V1BridgeInitContext
        {
            Parameters = parameters,
            HttpClient = httpClient,
            FileSystem = fs,
            TransportFactory = transportFactory,
            Logger = logger,
        };

        try
        {
            await pipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Bridge v1: 初始化失败");
            context.Fail(ex.Message);
        }

        return context.Failed ? null : context.Handle;
    }

    #endregion
}
