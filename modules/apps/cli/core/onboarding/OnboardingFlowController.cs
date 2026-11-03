namespace JoinCode.Cli;

/// <summary>
/// Onboarding 流程控制器 - 管理步骤导航、状态持久化和完成追踪
/// </summary>
[Register(typeof(IOnboardingService), ServiceLifetime.Singleton)]
public sealed partial class OnboardingFlowController : ServiceEntity, IOnboardingService, IDisposable {
    private const int TotalStepCount = 4;

    private static readonly OnboardingStep[] Steps =
    [
        OnboardingStep.Welcome,
        OnboardingStep.ApiKey,
        OnboardingStep.Security,
        OnboardingStep.TerminalSetup
    ];

    private readonly OnboardingStatePersistence _persistence;
    private bool _disposed;

    private sealed record FlowState(
        OnboardingStep CurrentStep,
        int CurrentStepIndex,
        int SelectedIndex,
        bool IsComplete,
        string? ApiKey
    ) {
        public static readonly FlowState Initial = new(OnboardingStep.Welcome, 0, 0, false, null);
    }

    private FlowState _flowState = FlowState.Initial;

    /// <inheritdoc />
    public bool IsOnboardingComplete => Volatile.Read(ref _flowState).IsComplete;

    /// <inheritdoc />
    public OnboardingState CurrentState {
        get {
            var s = Volatile.Read(ref _flowState);
            return new OnboardingState {
                CurrentStep = s.CurrentStep,
                TotalSteps = TotalStepCount,
                CurrentStepIndex = s.CurrentStepIndex,
            };
        }
    }

    /// <inheritdoc />
    public event EventHandler<OnboardingStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造函数 — 注入引导状态持久化器
    /// </summary>
    /// <param name="persistence">用于读写引导完成状态的持久化器</param>
    public OnboardingFlowController(OnboardingStatePersistence persistence) {
        _persistence = persistence;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        var isComplete = await _persistence.IsCompleteAsync(cancellationToken).ConfigureAwait(false);
        UpdateState(s => s with { IsComplete = isComplete });
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) {
        var (previous, next) = UpdateState(s =>
            s.IsComplete ? s : s with { CurrentStep = OnboardingStep.Welcome, CurrentStepIndex = 0, SelectedIndex = 0, ApiKey = null });
        RaiseStateChanged(previous.CurrentStep, next.CurrentStep);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NextStepAsync(CancellationToken cancellationToken = default) {
        var (previous, next) = UpdateState(s => {
            if (s.IsComplete) return s;
            var currentIndex = Array.IndexOf(Steps, s.CurrentStep);
            if (currentIndex < 0 || currentIndex >= Steps.Length - 1) {
                if (s.CurrentStep == OnboardingStep.Complete) return s;
                return s with { CurrentStep = OnboardingStep.Complete, CurrentStepIndex = TotalStepCount };
            }
            return s with { CurrentStepIndex = currentIndex + 1, CurrentStep = Steps[currentIndex + 1] };
        });
        RaiseStateChanged(previous.CurrentStep, next.CurrentStep);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PreviousStepAsync(CancellationToken cancellationToken = default) {
        var (previous, next) = UpdateState(s => {
            if (s.IsComplete) return s;
            if (s.CurrentStep == OnboardingStep.Welcome) return s;

            if (s.CurrentStep == OnboardingStep.Complete)
                return s with { CurrentStep = OnboardingStep.TerminalSetup, CurrentStepIndex = TotalStepCount - 1 };

            var currentIndex = Array.IndexOf(Steps, s.CurrentStep);
            if (currentIndex > 0)
                return s with { CurrentStepIndex = currentIndex - 1, CurrentStep = Steps[currentIndex - 1] };
            return s;
        });
        RaiseStateChanged(previous.CurrentStep, next.CurrentStep);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(CancellationToken cancellationToken = default) {
        var (previous, _) = UpdateState(s => s with { CurrentStep = OnboardingStep.Complete, CurrentStepIndex = TotalStepCount, IsComplete = true });
        await _persistence.MarkCompleteAsync(cancellationToken).ConfigureAwait(false);
        RaiseStateChanged(previous.CurrentStep, OnboardingStep.Complete);
    }

    /// <inheritdoc />
    public async Task SkipAsync(CancellationToken cancellationToken = default) {
        var (previous, _) = UpdateState(s => s with { CurrentStep = OnboardingStep.Complete, CurrentStepIndex = TotalStepCount, IsComplete = true });
        await _persistence.MarkCompleteAsync(cancellationToken).ConfigureAwait(false);
        RaiseStateChanged(previous.CurrentStep, OnboardingStep.Complete);
    }

    /// <inheritdoc />
    public Task SetApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new ArgumentException("API key cannot be null or whitespace.", nameof(apiKey));
        }

        UpdateState(s => s with { ApiKey = apiKey });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SelectTerminalSetupOptionAsync(int optionIndex, CancellationToken cancellationToken = default) {
        if (optionIndex < 0) {
            throw new ArgumentOutOfRangeException(nameof(optionIndex), "Option index cannot be negative.");
        }

        UpdateState(s => s with { SelectedIndex = optionIndex });
        return Task.CompletedTask;
    }

    private (FlowState previous, FlowState next) UpdateState(Func<FlowState, FlowState> updater) {
        while (true) {
            var current = Volatile.Read(ref _flowState);
            var next = updater(current);
            if (ReferenceEquals(next, current)) return (current, current);
            if (Interlocked.CompareExchange(ref _flowState, next, current) == current) return (current, next);
        }
    }

    private void RaiseStateChanged(OnboardingStep previous, OnboardingStep current) {
        if (previous != current) {
            StateChanged?.Invoke(this, new OnboardingStateChangedEventArgs {
                PreviousStep = previous,
                CurrentStep = current
            });
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose() {
        if (_disposed) return; _disposed = true;
        base.Dispose();
    }
}
