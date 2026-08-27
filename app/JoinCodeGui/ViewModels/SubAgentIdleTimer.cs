namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 子代理中断后的空闲倒计时器 — 双击 ESC 中断子代理后启动，60 秒无输入活动则触发 mainAgent 接手。
/// 用户打字（KeyDown）重置倒计时（不恢复子代理）；用户发送消息取消倒计时（立即恢复子代理）。
/// 对齐 PRD 3.2 空闲超时语义：60秒无任何输入活动才唤醒 mainAgent，任何打字活动都重置倒计时。
/// </summary>
public sealed class SubAgentIdleTimer : IDisposable
{
    private readonly Avalonia.Threading.DispatcherTimer _timer;
    private readonly Action<int>? _onTick;
    private readonly int _timeoutSeconds;
    private string? _teammateId;
    private int _remainingSeconds;
    private bool _disposed;

    /// <summary>倒计时归零时触发 — 参数为 teammateId，GUI 层订阅后执行 Cancel + mainAgent 接手编排</summary>
    public event EventHandler<string>? MainAgentTakeoverRequested;

    /// <summary>
    /// 构造倒计时器。
    /// </summary>
    /// <param name="timeoutSeconds">空闲超时秒数，0 = 禁用（永不唤醒 mainAgent，纯对齐 ClaudeCode）</param>
    /// <param name="onTick">每秒回调更新 UI 剩余秒数（可 null）</param>
    public SubAgentIdleTimer(int timeoutSeconds = 60, Action<int>? onTick = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _onTick = onTick;
        _timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>启动倒计时 — Interrupt 子代理后调用</summary>
    public void Start(string teammateId)
    {
        if (_timeoutSeconds <= 0) return;
        _teammateId = teammateId;
        _remainingSeconds = _timeoutSeconds;
        _timer.Start();
        _onTick?.Invoke(_remainingSeconds);
    }

    /// <summary>重置倒计时 — 用户打字（KeyDown，未发送）时调用，用户还在活动别打断</summary>
    public void Reset()
    {
        if (!_timer.IsEnabled) return;
        _remainingSeconds = _timeoutSeconds;
        _onTick?.Invoke(_remainingSeconds);
    }

    /// <summary>停止倒计时 — 用户发送消息时调用（立即恢复子代理，取消超时移交）</summary>
    public void Stop()
    {
        _timer.Stop();
        _onTick?.Invoke(0);
    }

    /// <summary>当前是否正在倒计时</summary>
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>当前剩余秒数</summary>
    public int RemainingSeconds => _remainingSeconds;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        _onTick?.Invoke(_remainingSeconds);
        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            if (_teammateId is not null)
            {
                MainAgentTakeoverRequested?.Invoke(this, _teammateId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
