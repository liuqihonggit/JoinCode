namespace Sync.McpToolHandlers.Services;

/// <summary>
/// Channel 状态服务实现
/// 对齐 TS bootstrap/state.ts getAllowedChannels / isChannelsEnabled
/// 当前为基础实现，返回无活跃 channels
/// 后续实现 --channels 命令行参数和 MCP channel 注册时扩展
/// </summary>
[Register]
public sealed partial class ChannelStateService : IChannelStateService
{
    private volatile IReadOnlyList<ChannelEntry> _allowedChannels = Array.Empty<ChannelEntry>();

    /// <inheritdoc />
    public bool IsChannelsEnabled => _allowedChannels.Count > 0;

    /// <inheritdoc />
    public IReadOnlyList<ChannelEntry> GetAllowedChannels() => _allowedChannels;

    /// <inheritdoc />
    public void SetAllowedChannels(IReadOnlyList<ChannelEntry> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _allowedChannels = channels;
    }
}
