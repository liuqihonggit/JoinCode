
namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;
using Infrastructure.Pipeline;
using Core.Bridge.Init;

public static partial class BridgeRemoteCore
{
    #region initEnvLessBridgeCore — 管道化

    /// <summary>
    /// 初始化 Env-less 桥核心 — 对齐 TS 端 initEnvLessBridgeCore
    /// 通过中间件管道执行，消除 try-catch 样板代码
    /// </summary>
    public static async Task<IReplBridgeHandle?> InitEnvLessBridgeCoreAsync(
        BridgeEnvLessParams parameters,
        HttpClient httpClient,
        IReplBridgeTransportFactory transportFactory,
        MiddlewarePipeline<V2BridgeInitContext> pipeline,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(pipeline);

        var context = new V2BridgeInitContext
        {
            Parameters = parameters,
            HttpClient = httpClient,
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
            logger?.LogError(ex, "Bridge: 初始化失败");
            context.Fail(ex.Message);
        }

        return context.Failed ? null : context.Handle;
    }

    #endregion
}
