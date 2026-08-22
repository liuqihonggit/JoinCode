
namespace Core.Bridge;

/// <summary>
/// 远程控制桥核心 — 对齐 TS 端 remoteBridgeCore.ts
/// v2 env-less 桥核心：跳过 Environments API，直接通过 /bridge 端点获取 worker_jwt
/// 
/// 已拆分为以下 partial 文件：
/// - BridgeRemoteCore.Helpers.cs       (FetchCredentialsWithDeviceToken + WithRetry + DeriveTitle)
/// - BridgeRemoteCore.V1Init.cs        (InitBridgeCoreAsync — v1 env-based 初始化)
/// - BridgeRemoteCore.V1Callbacks.cs   (WireV1TransportCallbacks)
/// - BridgeRemoteCore.V2Callbacks.cs   (WireV2TransportCallbacks + ConvertWsUrlToPostUrl + ReconnectEnvironmentWithSessionAsync)
/// - BridgeRemoteCore.V2Init.cs        (InitEnvLessBridgeCoreAsync — v2 env-less 初始化)
/// - BridgeRemoteCore.FlushHistory.cs  (FlushHistoryAsync + DrainFlushGate)
/// - BridgeRemoteCore.AuthRecovery.cs  (RecoverFromAuthFailure + RebuildTransport + WireTransportCallbacks)
/// </summary>
public static partial class BridgeRemoteCore
{
}
