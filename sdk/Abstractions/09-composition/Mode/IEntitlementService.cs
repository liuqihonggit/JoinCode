namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 功能权限服务接口 — 对齐 TS isBriefEntitled()/isBriefEnabled() 模式
/// 开源项目默认允许所有功能，但保留接口以便扩展（如订阅制、远程特性开关等）
/// </summary>
public interface IEntitlementService
{
    /// <summary>
    /// Brief 模式是否有权限使用 — 对齐 TS isBriefEntitled()
    /// 开源项目默认 true；可通过 JCC_BRIEF 环境变量控制
    /// </summary>
    bool IsBriefEntitled { get; }

    /// <summary>
    /// Brief 工具是否当前激活 — 对齐 TS isBriefEnabled()
    /// 需要 entitlement 权限 + 用户 opt-in
    /// </summary>
    bool IsBriefEnabled { get; }
}
