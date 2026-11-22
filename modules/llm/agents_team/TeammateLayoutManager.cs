namespace Core.Agents.Coordinator;

/// <summary>队友布局管理器 — 管理多队友在终端中的窗格布局、颜色分配与位置调度</summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.ITeammateLayoutManager), ServiceLifetime.Singleton)]
public sealed partial class TeammateLayoutManager : ServiceEntity, JoinCode.Abstractions.Interfaces.ITeammateLayoutManager {
    private static readonly string[] AgentColors =
    new[] {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4",
        "#FFEAA7", "#DDA0DD", "#98D8C8", "#F7DC6F",
        "#BB8FCE", "#85C1E9", "#F8C471", "#82E0AA"
     };

    private readonly JoinCode.Abstractions.Interfaces.IPaneBackend _backend;
    private readonly ILogger<TeammateLayoutManager>? _logger;
    private readonly ConcurrentDictionary<string, TeammateEntry> _teammates = new(StringComparer.Ordinal);
    private int _colorIndex;

    private record TeammateEntry(string PaneId, string Color);

    /// <summary>
    /// 当前面板后端类型
    /// </summary>
    public JoinCode.Abstractions.Interfaces.BackendType CurrentBackendType => _backend.BackendType;

    /// <summary>
    /// 初始化 Teammate 面板布局管理器
    /// </summary>
    /// <param name="backend">面板后端</param>
    /// <param name="logger">日志记录器</param>
    public TeammateLayoutManager(
        JoinCode.Abstractions.Interfaces.IPaneBackend backend,
        ILogger<TeammateLayoutManager>? logger = null) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger;
    }

    /// <summary>
    /// 异步为 Teammate 创建面板并设置边框颜色与标题
    /// </summary>
    /// <param name="teammateId">Teammate 标识</param>
    /// <param name="agentType">智能体类型</param>
    /// <param name="command">启动命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建面板结果</returns>
    public async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string agentType, string command, CancellationToken cancellationToken = default) {

        var result = await _backend.CreateTeammatePaneAsync(teammateId, command, cancellationToken).ConfigureAwait(false);
        var color = AssignTeammateColor(teammateId);
        _teammates[teammateId] = new TeammateEntry(result.PaneId, color);
        await _backend.SetPaneBorderColorAsync(result.PaneId, color, cancellationToken).ConfigureAwait(false);
        await _backend.SetPaneTitleAsync(result.PaneId, $"{agentType}:{teammateId[..Math.Min(8, teammateId.Length)]}", cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Created teammate pane: TeammateId={TeammateId}, PaneId={PaneId}, Backend={Backend}",
            teammateId, result.PaneId, result.BackendType);

        return result;

    }

    /// <summary>
    /// 为指定 Teammate 分配颜色（已分配则返回既有颜色）
    /// </summary>
    /// <param name="teammateId">Teammate 标识</param>
    /// <returns>分配的颜色十六进制字符串</returns>
    public string AssignTeammateColor(string teammateId) {
        if (_teammates.TryGetValue(teammateId, out var existing))
            return existing.Color;

        var index = Interlocked.Increment(ref _colorIndex) - 1;
        return AgentColors[index % AgentColors.Length];
    }

    /// <summary>
    /// 异步移除指定 Teammate 的面板，并在剩余面板存在时重新平衡布局
    /// </summary>
    /// <param name="teammateId">Teammate 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RemoveTeammatePaneAsync(string teammateId, CancellationToken cancellationToken = default) {

        if (_teammates.TryGetValue(teammateId, out var entry)) {
            await _backend.KillPaneAsync(entry.PaneId, cancellationToken).ConfigureAwait(false);
            _teammates.TryRemove(teammateId, out _);

            if (_teammates.Count > 0)
                await _backend.RebalancePanesAsync(cancellationToken).ConfigureAwait(false);
        }

    }

    /// <summary>
    /// 异步重新平衡所有 Teammate 面板的布局
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RebalanceLayoutAsync(CancellationToken cancellationToken = default) {

        await _backend.RebalancePanesAsync(cancellationToken).ConfigureAwait(false);

    }

    /// <summary>释放资源 — 释放布局管理锁</summary>
    public override void Dispose() {
        base.Dispose();
    }
}