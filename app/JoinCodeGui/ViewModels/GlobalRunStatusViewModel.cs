using System.ComponentModel;

namespace JoinCode.Gui.ViewModels;

/// <summary>卡死检测状态机状态 — 规则8风格显式枚举</summary>
public enum StallDetectionState
{
    /// <summary>正常监测（回合未开始或心跳新鲜/有活跃工具）</summary>
    Monitoring,
    /// <summary>疑似卡死：超过阈值无心跳且无活跃工具</summary>
    Stalled
}

/// <summary>
/// 全局运行状态条 VM — 底部状态条的运行聚合区：
/// 随机动词 spinner + 回合耗时 + token 聚合 + "N 个后台代理"入口 + 卡死渐变红。
/// 时钟经构造注入（测试可控）；定时回调由 View 层 DispatcherTimer 驱动
/// <see cref="OnHeartbeatTick"/>，热路径只有此小控件，不进消息列表绑定。
/// </summary>
public sealed class GlobalRunStatusViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>卡死判定阈值 — 对齐 ClaudeCode useStalledAnimation 的 3s 无心跳</summary>
    public const double StallThresholdSeconds = 3;

    private readonly Func<DateTime> _clock;
    private DateTime _lastHeartbeatUtc;
    private bool _hasActiveTool;
    private DateTime _turnStartedAtUtc;
    private long _totalTokens;

    public GlobalRunStatusViewModel(Func<DateTime>? clock = null)
        => _clock = clock ?? (() => DateTime.UtcNow);

    // === 状态机 ===

    private StallDetectionState _stallState = StallDetectionState.Monitoring;
    public StallDetectionState StallState
    {
        get => _stallState;
        private set
        {
            if (_stallState == value)
                return;
            _stallState = value;
            Raise(nameof(StallState));
            Raise(nameof(IsStalled));
            Raise(nameof(StatusGlyph));
        }
    }

    /// <summary>是否卡死（驱动状态点渐变红）</summary>
    public bool IsStalled => StallState == StallDetectionState.Stalled;

    // === 绑定属性 ===

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (_isBusy != value) { _isBusy = value; Raise(nameof(IsBusy)); } } }

    private string _verb = string.Empty;
    public string Verb { get => _verb; private set { if (_verb != value) { _verb = value; Raise(nameof(Verb)); } } }

    private string _elapsedText = string.Empty;
    public string ElapsedText { get => _elapsedText; private set { if (_elapsedText != value) { _elapsedText = value; Raise(nameof(ElapsedText)); } } }

    private string _tokenText = string.Empty;
    public string TokenText { get => _tokenText; private set { if (_tokenText != value) { _tokenText = value; Raise(nameof(TokenText)); } } }

    private string _backgroundPillText = string.Empty;
    public string BackgroundPillText { get => _backgroundPillText; private set { if (_backgroundPillText != value) { _backgroundPillText = value; Raise(nameof(BackgroundPillText)); } } }

    /// <summary>状态点字符：运行 ⟳ / 结束 ✓（卡死时由 IsStalled 驱动变色）</summary>
    public string StatusGlyph => IsBusy ? "⟳" : "✓";

    /// <summary>随机动词表 — 每回合采样一个，可后续接入 settings 自定义</summary>
    private static readonly string[] SpinnerVerbs =
    [
        "推演中", "编织中", "检索中", "推敲中", "梳理中", "构思中",
        "打磨中", "勘误中", "穿针引线", "翻箱倒柜", "抽丝剥茧", "运筹帷幄",
        "凝神静气", "灵光乍现", "步步为营", "顺藤摸瓜", "集思广益", "精雕细琢"
    ];

    private static readonly Random Rng = new();

    // === 回合生命周期 ===

    /// <summary>回合开始 — 采样动词、复位状态机与统计</summary>
    public void StartTurn()
    {
        _turnStartedAtUtc = _clock();
        _lastHeartbeatUtc = _turnStartedAtUtc;
        _hasActiveTool = false;
        _totalTokens = 0;
        IsBusy = true;
        StallState = StallDetectionState.Monitoring;
        Verb = SpinnerVerbs[Rng.Next(SpinnerVerbs.Length)];
        ElapsedText = FormatElapsed(TimeSpan.Zero);
        TokenText = string.Empty;
        Raise(nameof(StatusGlyph));
    }

    /// <summary>回合结束 — 定格耗时、退出卡死态</summary>
    public void EndTurn()
    {
        RefreshElapsed();
        IsBusy = false;
        StallState = StallDetectionState.Monitoring;
        Raise(nameof(StatusGlyph));
    }

    /// <summary>
    /// 心跳上报 — 每条引擎事件到达时调用。
    /// hasActiveTool=true（工具执行中）豁免卡死检测，对齐 ClaudeCode 行为
    /// </summary>
    public void ReportActivity(bool hasActiveTool)
    {
        _lastHeartbeatUtc = _clock();
        _hasActiveTool = hasActiveTool;
        if (IsStalled)
            StallState = StallDetectionState.Monitoring;
    }

    /// <summary>聚合 token（Complete 事件的真实用量累加）</summary>
    public void AddTokens(long totalTokens)
    {
        if (totalTokens <= 0)
            return;
        _totalTokens += totalTokens;
        TokenText = FormatTokens(_totalTokens);
    }

    /// <summary>后台代理计数变化（fork 启动/完成回填驱动）</summary>
    public void SetBackgroundCount(int count)
        => BackgroundPillText = count > 0 ? $"{count} 个后台代理" : string.Empty;

    /// <summary>定时器回调（View 层 ~500ms 调度）：刷新耗时 + 卡死判定转移</summary>
    public void OnHeartbeatTick()
    {
        if (!IsBusy)
            return;

        RefreshElapsed();
        var idleSeconds = (_clock() - _lastHeartbeatUtc).TotalSeconds;
        var next = !_hasActiveTool && idleSeconds > StallThresholdSeconds
            ? StallDetectionState.Stalled
            : StallDetectionState.Monitoring;
        StallState = next;
    }

    private void RefreshElapsed()
        => ElapsedText = FormatElapsed(_clock() - _turnStartedAtUtc);

    internal static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1
            ? $"· {(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"· {elapsed.TotalSeconds:F0}s";

    internal static string FormatTokens(long tokens)
        => tokens >= 1000
            ? $"· ↓ {tokens / 1000.0:0.#}k tokens"
            : $"· ↓ {tokens} tokens";
}
