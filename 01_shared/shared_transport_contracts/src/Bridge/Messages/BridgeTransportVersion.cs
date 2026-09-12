namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层版本 — 对齐 TS 端 v1/v2 选择
/// </summary>
public enum BridgeTransportVersion
{
    /// <summary>v1: HybridTransport（WS 读 + HTTP POST 写到 Session-Ingress）</summary>
    [EnumValue("v1")] V1,

    /// <summary>v2: SSETransport（读）+ CCRClient（写到 CCR v2 /worker/*）</summary>
    [EnumValue("v2")] V2
}
