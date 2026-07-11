namespace JoinCode.Cli;

/// <summary>
/// Onboarding 流程控制器 - 管理步骤导航、状态持久化和完成追踪
/// </summary>
[Register]
public sealed partial class OnboardingFlowController : IOnboardingService, IDisposable
{
    private const int TotalStepCount = 4;

    private static readonly OnboardingStep[] Steps =
    [
        OnboardingStep.Welcome,
        OnboardingStep.ApiKey,
        OnboardingStep.Security,
        OnboardingStep.TerminalSetup
    ];

    private readonly OnboardingStatePersistence _persistence;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OnboardingStep _currentStep = OnboardingStep.Welcome;
    private int _currentStepIndex;
    private int _selectedIndex;
    private bool _isOnboardingComplete;
    private string? _apiKey;

    /// <inheritdoc />
    public bool IsOnboardingComplete
    {
        get
        {
            if (!_lock.Wait(0)) return Volatile.Read(ref _isOnboardingComplete);
            try { return _isOnboardingComplete; }
            finally { _lock.Release(); }
        }
    }

    /// <inheritdoc />
    public OnboardingState CurrentState
    {
        get
        {
            if (!_lock.Wait(0)) return BuildStateUnsafe();
            try { return BuildState(); }
            finally { _lock.Release(); }
        }
    }

    /// <inheritdoc />
    public event EventHandler<OnboardingStateChangedEventArgs>? StateChanged;

    public OnboardingFlowController(OnboardingStatePersistence persistence)
    {
        _persistence = persistence;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isOnboardingComplete = await _persistence.IsCompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        OnboardingStep previous;
        if (!_lock.Wait(0)) return Task.CompletedTask;
        try
        {
            if (_isOnboardingComplete) return Task.CompletedTask;
            previous = _currentStep;
            _currentStep = OnboardingStep.Welcome;
            _currentStepIndex = 0;
            _selectedIndex = 0;
            _apiKey = null;
        }
        finally { _lock.Release(); }

        RaiseStateChanged(previous, OnboardingStep.Welcome);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NextStepAsync(CancellationToken cancellationToken = default)
    {
        OnboardingStep previous;
        OnboardingStep next;

        if (!_lock.Wait(0)) return Task.CompletedTask;
        try
        {
            if (_isOnboardingComplete) return Task.CompletedTask;
            previous = _currentStep;

            var currentIndex = Array.IndexOf(Steps, _currentStep);
            if (currentIndex < 0 || currentIndex >= Steps.Length - 1)
            {
                if (_currentStep == OnboardingStep.Complete) return Task.CompletedTask;
                next = OnboardingStep.Complete;
                _currentStep = next;
                _currentStepIndex = TotalStepCount;
            }
            else
            {
                _currentStepIndex = currentIndex + 1;
                _currentStep = Steps[_currentStepIndex];
                next = _currentStep;
            }
        }
        finally { _lock.Release(); }

        RaiseStateChanged(previous, next);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PreviousStepAsync(CancellationToken cancellationToken = default)
    {
        OnboardingStep previous;
        OnboardingStep next;

        if (!_lock.Wait(0)) return Task.CompletedTask;
        try
        {
            if (_isOnboardingComplete) return Task.CompletedTask;
            previous = _currentStep;

            if (_currentStep == OnboardingStep.Welcome) return Task.CompletedTask;

            if (_currentStep == OnboardingStep.Complete)
            {
                _currentStep = OnboardingStep.TerminalSetup;
                _currentStepIndex = TotalStepCount - 1;
                next = _currentStep;
            }
            else
            {
                var currentIndex = Array.IndexOf(Steps, _currentStep);
                if (currentIndex > 0)
                {
                    _currentStepIndex = currentIndex - 1;
                    _currentStep = Steps[_currentStepIndex];
                    next = _currentStep;
                }
                else
                {
                    return Task.CompletedTask;
                }
            }
        }
        finally { _lock.Release(); }

        RaiseStateChanged(previous, next);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        OnboardingStep previous;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previous = _currentStep;
            _currentStep = OnboardingStep.Complete;
            _currentStepIndex = TotalStepCount;
            _isOnboardingComplete = true;
        }
        finally
        {
            _lock.Release();
        }

        await _persistence.MarkCompleteAsync(cancellationToken).ConfigureAwait(false);
        RaiseStateChanged(previous, OnboardingStep.Complete);
    }

    /// <inheritdoc />
    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        OnboardingStep previous;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previous = _currentStep;
            _currentStep = OnboardingStep.Complete;
            _currentStepIndex = TotalStepCount;
            _isOnboardingComplete = true;
        }
        finally
        {
            _lock.Release();
        }

        await _persistence.MarkCompleteAsync(cancellationToken).ConfigureAwait(false);
        RaiseStateChanged(previous, OnboardingStep.Complete);
    }

    /// <inheritdoc />
    public Task SetApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or whitespace.", nameof(apiKey));
        }

        if (!_lock.Wait(0)) return Task.CompletedTask;
        try { _apiKey = apiKey; }
        finally { _lock.Release(); }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SelectTerminalSetupOptionAsync(int optionIndex, CancellationToken cancellationToken = default)
    {
        if (optionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(optionIndex), "Option index cannot be negative.");
        }

        if (!_lock.Wait(0)) return Task.CompletedTask;
        try { _selectedIndex = optionIndex; }
        finally { _lock.Release(); }

        return Task.CompletedTask;
    }

    private OnboardingState BuildState()
    {
        return new OnboardingState
        {
            CurrentStep = _currentStep,
            TotalSteps = TotalStepCount,
            CurrentStepIndex = _currentStepIndex,
        };
    }

    private OnboardingState BuildStateUnsafe()
    {
        return new OnboardingState
        {
            CurrentStep = _currentStep,
            TotalSteps = TotalStepCount,
            CurrentStepIndex = _currentStepIndex,
        };
    }

    private void RaiseStateChanged(OnboardingStep previous, OnboardingStep current)
    {
        if (previous != current)
        {
            StateChanged?.Invoke(this, new OnboardingStateChangedEventArgs
            {
                PreviousStep = previous,
                CurrentStep = current
            });
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
