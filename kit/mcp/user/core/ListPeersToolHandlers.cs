

namespace McpToolDispatch;

/// <summary>
/// 节点列表工具处理器 — 列出已连接的对等节点
/// </summary>
[McpToolDispatch(ToolCategory.Peers, Optional = true)]
public partial class ListPeersToolHandlers
{
    private readonly ILogger<ListPeersToolHandlers>? _logger;
    private readonly IPeerDiscoveryService? _peerService;

    /// <summary>
    /// 初始化节点列表工具处理器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="peerService">节点发现服务（可选）</param>
    public ListPeersToolHandlers(ILogger<ListPeersToolHandlers>? logger = null, IPeerDiscoveryService? peerService = null)
    {
        _logger = logger;
        _peerService = peerService;
    }

    /// <summary>
    /// 列出已连接的对等节点
    /// </summary>
    /// <param name="filter">过滤条件（可选，默认 all）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含节点列表的工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.ListPeers, StringKey.ListPeersDesc, "network")]
    public async Task<ToolResult> ListPeersAsync(
        [McpToolParameter(StringKey.ListPeersFilterDesc, Required = false)] string filter = "all",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.PeerListTitle));
            response.AppendLine();

            if (_peerService == null)
            {
                response.AppendLine(L.T(StringKey.PeerDiscoveryNotEnabled));
                response.AppendLine(L.T(StringKey.EnsureBridgeRunning));
                return ToolResultBuilder.Success().WithText(response.ToString()).Build();
            }

            var peers = _peerService.GetConnectedPeers();

            if (!peers.Any())
            {
                response.AppendLine(L.T(StringKey.NoConnectedPeers));
                response.AppendLine();
                response.AppendLine(L.T(StringKey.PeerAutoAppearHint));
            }
            else
            {
                response.AppendLine(L.T(StringKey.ConnectedPeerCount, peers.Count()));
                response.AppendLine();

                foreach (var peer in peers)
                {
                    var connectedAge = DateTime.UtcNow - peer.ConnectedAt;
                    response.AppendLine($"  [{peer.Id}] {peer.Name}");
                    response.AppendLine(L.T(StringKey.LabelConnectedAt, $"{peer.ConnectedAt:yyyy-MM-dd HH:mm:ss}", FormatDuration(connectedAge)));
                }
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.ListPeersFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ListPeersFailed, ex.Message)).Build();
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1) return L.T(StringKey.DurationSeconds, duration.Seconds);
        if (duration.TotalHours < 1) return L.T(StringKey.DurationMinutesSeconds, duration.Minutes, duration.Seconds);
        return L.T(StringKey.DurationHoursMinutes, (int)duration.TotalHours, duration.Minutes);
    }
}
