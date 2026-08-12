
namespace Core.Goal;

/// <summary>
/// 多目标注册表 — 管理多个 GoalEngine 实例，支持多 goal 并发和持久化恢复。
/// 对齐 PersistentDreamTaskRegistry 模式：内存缓存 + 持久化 + 启动恢复。
/// </summary>
[Register]
public sealed partial class PersistentGoalRegistry : IGoalRegistry
{
    private readonly Dictionary<string, GoalEngine> _engines = new();
    private readonly IServiceProvider _serviceProvider;
    [Inject] private readonly IGoalStateStore? _stateStore = null;
    [Inject] private readonly ILogger<PersistentGoalRegistry>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _currentGoalId;

    public PersistentGoalRegistry(
        IServiceProvider serviceProvider,
        IGoalStateStore? stateStore = null,
        ILogger<PersistentGoalRegistry>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public IGoalEngine? CurrentEngine => _currentGoalId is not null && _engines.TryGetValue(_currentGoalId, out var e) ? e : null;

    /// <inheritdoc />
    public async Task<GoalState> StartAsync(string objective, List<string>? constraints = null, int? tokenBudget = null, CancellationToken cancellationToken = default)
    {
        var engine = CreateEngine();
        var state = await engine.StartAsync(objective, constraints, tokenBudget, cancellationToken: cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _engines[state.GoalId] = engine;
            _currentGoalId = state.GoalId;
        }
        finally
        {
            _lock.Release();
        }

        _logger?.LogInformation("[PersistentGoalRegistry] 启动目标: {GoalId}", state.GoalId);
        return state;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoalState>> ListActiveGoalsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _engines.Values
                .Where(e => e.CurrentState is not null)
                .Select(e => e.CurrentState!)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public IGoalEngine? GetEngine(string goalId)
    {
        return _engines.TryGetValue(goalId, out var e) ? e : null;
    }

    /// <inheritdoc />
    public bool SetCurrent(string goalId)
    {
        if (!_engines.ContainsKey(goalId)) return false;
        _currentGoalId = goalId;
        return true;
    }

    /// <inheritdoc />
    public async Task RehydrateAllAsync(CancellationToken cancellationToken = default)
    {
        if (_stateStore is null) return;
        try
        {
            var activeGoals = await _stateStore.GetActiveGoalsAsync(cancellationToken).ConfigureAwait(false);
            if (activeGoals.Count == 0) return;

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var state in activeGoals)
                {
                    if (_engines.ContainsKey(state.GoalId)) continue;
                    var engine = CreateEngine();
                    await engine.RehydrateAsync(cancellationToken, state.GoalId).ConfigureAwait(false);
                    _engines[state.GoalId] = engine;
                }
                _currentGoalId ??= activeGoals[0].GoalId;
            }
            finally
            {
                _lock.Release();
            }
            _logger?.LogInformation("[PersistentGoalRegistry] 恢复 {Count} 个活跃目标", activeGoals.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[PersistentGoalRegistry] 恢复目标失败");
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentEngine is { } engine)
            await engine.PauseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentEngine is { } engine)
            await engine.ResumeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentEngine is not { } engine) return;
        await engine.ClearAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_currentGoalId is not null)
            {
                _engines.Remove(_currentGoalId);
                _currentGoalId = _engines.Keys.FirstOrDefault();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private GoalEngine CreateEngine()
    {
        var kernel = _serviceProvider.GetRequiredService<IChatClient>();
        var evaluator = _serviceProvider.GetRequiredService<IGoalEvaluator>();
        var heartbeat = _serviceProvider.GetRequiredService<IGoalHeartbeat>();
        var logger = _serviceProvider.GetService<ILogger<GoalEngine>>();
        var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
        var permissionManager = _serviceProvider.GetService<IToolPermissionManager>();
        var middlewares = _serviceProvider.GetService<IEnumerable<IGoalLifecycleMiddleware>>();
        var clock = _serviceProvider.GetService<IClockService>();
        var stateStore = _serviceProvider.GetService<IGoalStateStore>();
        return new GoalEngine(kernel, evaluator, logger, loggerFactory, permissionManager, middlewares, heartbeat, clock, _serviceProvider, stateStore);
    }
}
