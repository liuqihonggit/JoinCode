namespace Core.Configuration;

/// <summary>
/// 快速模式服务 — Actor 邮箱模型,状态由 Consumer 线程独占(无锁),冷却计时器由 ActorTimers 驱动(回投消息)。
/// </summary>
[Register(typeof(IFastModeService), ServiceLifetime.Singleton)]
public sealed partial class FastModeService : ActorBase<FastModeCommand, FastModeChangedEvt>, IFastModeService {
    private bool _isActive;
    private string _fastModelId;
    private string _primaryModelId;
    private readonly TimeSpan _cooldownDuration;
    private readonly ILogger<FastModeService>? _logger;
    private const string CooldownTimerKey = "cooldown";

    /// <summary>快速模式变更事件 — 激活/停用时触发</summary>
    public event EventHandler<FastModeChangedEventArgs>? FastModeChanged;

    /// <summary>
    /// 初始化快速模式服务
    /// </summary>
    /// <param name="config">可选的工作流配置(取主模型标识)</param>
    /// <param name="fastModelId">可选的快速模型标识</param>
    /// <param name="cooldownDuration">可选的冷却时长(默认 5 分钟)</param>
    /// <param name="logger">可选的日志记录器</param>
    /// <param name="modelConfigLoader">可选的模型配置加载器</param>
    public FastModeService(
        WorkflowConfig? config = null,
        string? fastModelId = null,
        TimeSpan? cooldownDuration = null,
        ILogger<FastModeService>? logger = null,
        IModelConfigLoader? modelConfigLoader = null) : base(logger: logger) {
        var loader = modelConfigLoader ?? new ModelConfigLoader();
        _primaryModelId = config?.Provider?.ModelId ?? loader.GetDefaultModelId(VendorKindEnumConstants.OpenAi);
        _fastModelId = fastModelId ?? loader.GetDefaultFastModelId(VendorKindEnumConstants.OpenAi);
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromMinutes(5);
        _logger = logger;
    }

    /// <inheritdoc />
    protected override void Handle(FastModeCommand cmd, CancellationToken ct) {
        switch (cmd) {
            case ActivateCmd:
                if (_isActive) break;
                _isActive = true;
                _logger?.LogInformation("Fast Mode activated: {FastModel}", _fastModelId);
                Timers.StartSingleTimer(CooldownTimerKey, new CooldownExpiredCmd(), _cooldownDuration);
                PublishChange(true);
                break;

            case DeactivateCmd:
                if (!_isActive) break;
                _isActive = false;
                Timers.Cancel(CooldownTimerKey);
                _logger?.LogInformation("Fast Mode deactivated: returning to {PrimaryModel}", _primaryModelId);
                PublishChange(false);
                break;

            case CooldownExpiredCmd:
                if (!_isActive) break;
                _isActive = false;
                _logger?.LogDebug("Fast Mode cooldown expired, auto-deactivating");
                PublishChange(false);
                break;

            case ToggleCmd:
                _isActive = !_isActive;
                if (_isActive) {
                    _logger?.LogInformation("Fast Mode activated: {FastModel}", _fastModelId);
                    Timers.StartSingleTimer(CooldownTimerKey, new CooldownExpiredCmd(), _cooldownDuration);
                } else {
                    _logger?.LogInformation("Fast Mode deactivated: returning to {PrimaryModel}", _primaryModelId);
                    Timers.Cancel(CooldownTimerKey);
                }
                PublishChange(_isActive);
                break;

            case SetFastModelCmd(var modelId):
                _fastModelId = modelId;
                _logger?.LogDebug("Fast model set to: {ModelId}", modelId);
                break;

            case SetPrimaryModelCmd(var modelId):
                _primaryModelId = modelId;
                _logger?.LogDebug("Primary model set to: {ModelId}", modelId);
                break;

            case GetIsActiveQuery(var reply): reply.TrySetResult(_isActive); break;
            case GetFastModelQuery(var reply): reply.TrySetResult(_fastModelId); break;
            case GetPrimaryModelQuery(var reply): reply.TrySetResult(_primaryModelId); break;
            case GetCurrentModelQuery(var reply): reply.TrySetResult(_isActive ? _fastModelId : _primaryModelId); break;
            case IsInCooldownQuery(var reply): reply.TrySetResult(_isActive && Timers.Count > 0); break;
        }
    }

    /// <summary>发布变更事件 — Actor 输出流 + .NET 事件双通道</summary>
    private void PublishChange(bool isActive) {
        var activeModel = isActive ? _fastModelId : _primaryModelId;
        var inactiveModel = isActive ? _primaryModelId : _fastModelId;
        TryPublish(new FastModeChangedEvt(isActive, activeModel, inactiveModel));
        FastModeChanged?.Invoke(this, new FastModeChangedEventArgs {
            IsFastModeActive = isActive,
            ActiveModelId = activeModel,
            InactiveModelId = inactiveModel
        });
    }

    /// <summary>快速模式是否当前激活</summary>
    public bool IsFastModeActive => QuerySync<bool>(static r => new GetIsActiveQuery(r));

    /// <summary>快速模型标识</summary>
    public string FastModelId => QuerySync<string>(static r => new GetFastModelQuery(r));

    /// <summary>主模型标识</summary>
    public string PrimaryModelId => QuerySync<string>(static r => new GetPrimaryModelQuery(r));

    /// <summary>激活快速模式 — 切换到快速模型并启动冷却计时器</summary>
    public void Activate() => TrySend(new ActivateCmd());

    /// <summary>停用快速模式 — 切换回主模型并停止冷却计时器</summary>
    public void Deactivate() => TrySend(new DeactivateCmd());

    /// <summary>切换快速模式开关 — 激活时停用,停用时激活</summary>
    public void Toggle() => TrySend(new ToggleCmd());

    /// <summary>设置快速模型标识</summary>
    /// <param name="modelId">模型标识</param>
    public void SetFastModel(string modelId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        TrySend(new SetFastModelCmd(modelId));
    }

    /// <summary>设置主模型标识</summary>
    /// <param name="modelId">模型标识</param>
    public void SetPrimaryModel(string modelId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        TrySend(new SetPrimaryModelCmd(modelId));
    }

    /// <summary>获取当前生效的模型标识 — 快速模式激活时返回快速模型,否则返回主模型</summary>
    public string GetCurrentModelId() => QuerySync<string>(static r => new GetCurrentModelQuery(r));

    /// <summary>是否处于冷却期 — 快速模式激活且冷却计时器仍在运行</summary>
    public bool IsInCooldown() => QuerySync<bool>(static r => new IsInCooldownQuery(r));

    /// <summary>同步查询 — 发命令到邮箱,阻塞等 Reply(禁止在 Actor Consumer 线程内调用,会死锁)</summary>
    private T QuerySync<T>(Func<TaskCompletionSource<T>, FastModeCommand> factory) {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrySend(factory(tcs));
        return tcs.Task.GetAwaiter().GetResult();
    }
}

/// <summary>快速模式命令基类</summary>
public abstract record FastModeCommand;

/// <summary>激活快速模式</summary>
public sealed record ActivateCmd : FastModeCommand;
/// <summary>停用快速模式</summary>
public sealed record DeactivateCmd : FastModeCommand;
/// <summary>切换快速模式</summary>
public sealed record ToggleCmd : FastModeCommand;
/// <summary>设置快速模型标识</summary>
public sealed record SetFastModelCmd(string ModelId) : FastModeCommand;
/// <summary>设置主模型标识</summary>
public sealed record SetPrimaryModelCmd(string ModelId) : FastModeCommand;
/// <summary>冷却计时器到期(ActorTimers 回投,内部消息)</summary>
internal sealed record CooldownExpiredCmd : FastModeCommand;

/// <summary>查询是否激活</summary>
public sealed record GetIsActiveQuery(TaskCompletionSource<bool> Reply) : FastModeCommand;
/// <summary>查询快速模型标识</summary>
public sealed record GetFastModelQuery(TaskCompletionSource<string> Reply) : FastModeCommand;
/// <summary>查询主模型标识</summary>
public sealed record GetPrimaryModelQuery(TaskCompletionSource<string> Reply) : FastModeCommand;
/// <summary>查询当前生效模型标识</summary>
public sealed record GetCurrentModelQuery(TaskCompletionSource<string> Reply) : FastModeCommand;
/// <summary>查询是否处于冷却期</summary>
public sealed record IsInCooldownQuery(TaskCompletionSource<bool> Reply) : FastModeCommand;

/// <summary>快速模式变更输出事件(Actor 输出流)</summary>
public sealed record FastModeChangedEvt(bool IsFastModeActive, string ActiveModelId, string InactiveModelId);
