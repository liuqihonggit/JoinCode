
namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;
using Infrastructure.Pipeline;
using Core.Bridge.Init;

public static partial class BridgeRemoteCore
{
    #region initBridgeCore (v1 env-based) — 管道化

    /// <summary>
    /// 初始化 v1 env-based 桥核心 — 对齐 TS 端 initBridgeCore
    /// 通过中间件管道执行，消除 try-catch 样板代码
    /// </summary>
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
