namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Channel 条目模型
/// 对齐 TS bootstrap/state.ts ChannelEntry
/// 表示一个已注册的外部消息通道（Telegram/Discord/Slack 等）
/// </summary>
public sealed record ChannelEntry
{
    /// <summary>
    /// 通道类型
    /// </summary>
    public required ChannelKind Kind { get; init; }

    /// <summary>
    /// 通道名称（MCP 服务器名或插件名）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 是否为开发模式通道
    /// </summary>
    public bool IsDev { get; init; }
}

/// <summary>
/// 通道类型
/// </summary>
public enum ChannelKind
{
    /// <summary>
    /// MCP 服务器通道
    /// </summary>
    Server,

    /// <summary>
    /// 插件通道
    /// </summary>
    Plugin,
}

/// <summary>
/// Channel 状态服务接口
/// 对齐 TS bootstrap/state.ts getAllowedChannels / isChannelsEnabled
/// 管理 MCP 外部消息通道的注册和状态查询
/// </summary>
public interface IChannelStateService
{
    /// <summary>
    /// 是否有活跃的 channels（对齐 TS isChannelsEnabled）
    /// 当 channels 激活时，PlanMode 应被禁用
    /// </summary>
    bool IsChannelsEnabled { get; }

    /// <summary>
    /// 获取所有已注册的 channels（对齐 TS getAllowedChannels）
    /// </summary>
    IReadOnlyList<ChannelEntry> GetAllowedChannels();

    /// <summary>
    /// 设置允许的 channels（对齐 TS setAllowedChannels）
    /// </summary>
    void SetAllowedChannels(IReadOnlyList<ChannelEntry> channels);
}
