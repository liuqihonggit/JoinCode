namespace Core.Context;

/// <summary>
/// 推理轮次记录 — 持久化每轮推理的完整上下文(文本/指纹/熵值/工具调用/计时/循环检测)
/// </summary>
public sealed record ReasoningRound
{
    /// <summary>轮次号(从 1 开始)</summary>
    public required int Turn { get; init; }

    /// <summary>轮次开始时间</summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>轮次结束时间</summary>
    public required DateTimeOffset EndTime { get; init; }

    /// <summary>持续时长</summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>推理响应文本</summary>
    public string? ResponseText { get; init; }

    /// <summary>思考内容</summary>
    public string? ThinkingText { get; init; }

    /// <summary>逻辑指纹(前缀+后缀 hash)</summary>
    public int? LogicFingerprint { get; init; }

    /// <summary>Shannon 信息熵</summary>
    public double? ShannonEntropy { get; init; }

    /// <summary>本轮工具调用列表(工具名)</summary>
    public IReadOnlyList<string>? ToolCalls { get; init; }

    /// <summary>是否检测到循环</summary>
    public bool IsLoopDetected { get; init; }

    /// <summary>循环检测原因</summary>
    public string? LoopReason { get; init; }
}

/// <summary>
/// 推理轮次记录器 — 用无锁 RingBuffer 存储最近 N 轮记录,支持快照查询
/// </summary>
[Register]
public sealed class ReasoningRoundRecorder
{
    private readonly RingBuffer<ReasoningRound> _rounds;
    private readonly TimeProvider _timeProvider;
    private int _currentTurn;
    private DateTimeOffset _currentStart;

    /// <summary>
    /// 初始化推理轮次记录器
    /// </summary>
    /// <param name="capacity">最多保留的轮次记录数(超出覆盖最旧)</param>
    /// <param name="timeProvider">时间提供者(测试可注入)</param>
    public ReasoningRoundRecorder(int capacity = 50, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _rounds = new RingBuffer<ReasoningRound>(RingBuffer<ReasoningRound>.RoundUpToPowerOfTwo(capacity));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 开始一轮推理记录
    /// </summary>
    public void StartRound()
    {
        _currentTurn++;
        _currentStart = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// 结束当前轮次,持久化完整记录
    /// </summary>
    public ReasoningRound EndRound(
        string? responseText = null,
        string? thinkingText = null,
        int? logicFingerprint = null,
        double? shannonEntropy = null,
        IReadOnlyList<string>? toolCalls = null,
        bool isLoopDetected = false,
        string? loopReason = null)
    {
        var now = _timeProvider.GetUtcNow();
        var round = new ReasoningRound
        {
            Turn = _currentTurn,
            StartTime = _currentStart,
            EndTime = now,
            ResponseText = responseText,
            ThinkingText = thinkingText,
            LogicFingerprint = logicFingerprint,
            ShannonEntropy = shannonEntropy,
            ToolCalls = toolCalls,
            IsLoopDetected = isLoopDetected,
            LoopReason = loopReason
        };
        _rounds.Add(round);
        return round;
    }

    /// <summary>
    /// 获取所有轮次记录的一致快照(从最旧到最新)
    /// </summary>
    public IReadOnlyList<ReasoningRound> GetRounds() => _rounds.ToArray();

    /// <summary>
    /// 当前记录数
    /// </summary>
    public int Count => _rounds.Count;

    /// <summary>
    /// 最大保留轮次数(向上取整到 2 次幂后)
    /// </summary>
    public int Capacity => _rounds.Capacity;

    /// <summary>
    /// 当前轮次号(未开始为 0)
    /// </summary>
    public int CurrentTurn => _currentTurn;

    /// <summary>
    /// 重置记录器
    /// </summary>
    public void Reset()
    {
        _rounds.Clear();
        _currentTurn = 0;
        _currentStart = default;
    }
}
