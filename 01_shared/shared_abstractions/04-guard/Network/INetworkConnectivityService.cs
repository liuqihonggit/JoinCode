namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络连接性服务 — 统一网络感知门面
/// <para>
/// 职责:系统级网络可用性检测、VPN 识别、多流接口枚举、路由判断、状态变化事件订阅
/// </para>
/// <para>
/// 消费方应仅依赖此接口,不再直接使用 IHttpProxyService 做网络判断
/// </para>
/// </summary>
public interface INetworkConnectivityService
{
    /// <summary>
    /// 当前网络连接状态(实时快照)
    /// </summary>
    NetworkConnectivityState CurrentState { get; }

    /// <summary>
    /// 系统级网络是否可用 — 基于 NetworkInterface.GetIsNetworkAvailable() + 活跃接口验证
    /// </summary>
    bool IsNetworkAvailable();

    /// <summary>
    /// VPN 是否活跃 — 接口名/描述匹配 + 进程检测 + 环境变量三重识别
    /// </summary>
    bool IsVpnActive();

    /// <summary>
    /// 获取所有活跃网络接口(多流) — 一台机器可同时有多个活跃接口(以太网+WiFi+VPN隧道)
    /// </summary>
    IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces();

    /// <summary>
    /// 当前流量出口路由 — 直连/VPN/代理
    /// </summary>
    NetworkRoute GetCurrentRoute();

    /// <summary>
    /// 网络状态变化事件 — 订阅后 VPN 开关/断网/重连时实时触发
    /// </summary>
    event EventHandler<NetworkConnectivityChangedEventArgs>? StateChanged;
}
