using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 信息熵减检测器 — 串行漏斗式纵深防御
/// 检测器按成本从低到高串行运行,任一触发即返回(不跑后续更昂贵的检测器):
///   Layer1: OutputLoopDetector      — 输出文本重复模式（尾部子串重复,最廉价）
///   Layer2: LogicFingerprintDetector — 逻辑指纹循环（前缀+后缀hash,中等）
///   Layer3: ToolCallSequenceDetector — 工具调用序列循环（工具名+参数指纹重复,中等）
///   Layer4: ShannonEntropyDetector   — Shannon信息熵持续下降（字符分布趋于集中,最昂贵）
/// 触发时通过 LoopDiagnosticJournal 记录追踪链，供医生模式回溯分析
/// </summary>
[Register]
public sealed class InformationEntropyGuardian : ServiceEntity, IOutputLoopDetector, ILoopDetectionStrategy
{
    private readonly OutputLoopDetector _outputLoopDetector;
    private readonly LogicFingerprintDetector _logicFingerprintDetector;
    private readonly ToolCallSequenceDetector _toolCallSequenceDetector;
    private readonly ShannonEntropyDetector _shannonEntropyDetector;
    private readonly LoopDiagnosticJournal _journal;
    private readonly ILogger? _logger;

    private string _sessionId = "";
    private int _conversationTurn;
    private int _toolCallCount;

    public InformationEntropyGuardian(
        OutputLoopDetector? outputLoopDetector = null,
        LogicFingerprintDetector? logicFingerprintDetector = null,
        ToolCallSequenceDetector? toolCallSequenceDetector = null,
        ShannonEntropyDetector? shannonEntropyDetector = null,
        LoopDiagnosticJournal? journal = null,
        ILogger? logger = null)
    {
        _outputLoopDetector = outputLoopDetector ?? new OutputLoopDetector();
        _logicFingerprintDetector = logicFingerprintDetector ?? new LogicFingerprintDetector();
        _toolCallSequenceDetector = toolCallSequenceDetector ?? new ToolCallSequenceDetector();
        _shannonEntropyDetector = shannonEntropyDetector ?? new ShannonEntropyDetector();
        _journal = journal ?? new LoopDiagnosticJournal(logger: logger);
        _logger = logger;
    }

    /// <summary>
    /// 设置当前会话上下文 — 供 QueryLoopMiddleware 在每轮开始时调用
    /// </summary>
    public void SetContext(string sessionId, int conversationTurn, int toolCallCount)
    {
        _sessionId = sessionId;
        _conversationTurn = conversationTurn;
        _toolCallCount = toolCallCount;
    }

    /// <summary>
    /// IOutputLoopDetector.Detect — 串行漏斗: OutputLoop(廉价)→LogicFingerprint(中等),任一触发即返回
    /// 注意：ShannonEntropy 不参与 Detect，因为 Detect 传入的是累积文本（不断增长），熵趋势无意义
    /// </summary>
    public LoopDetectionResult Detect(string accumulatedText)
    {
        if (string.IsNullOrEmpty(accumulatedText))
            return LoopDetectionResult.NoLoop;

        _journal.Record("guardian_detect", _sessionId, _conversationTurn, _toolCallCount,
            new Dictionary<string, string> { ["text_len"] = accumulatedText.Length.ToString() });

        var outputResult = _outputLoopDetector.Detect(accumulatedText);
        if (outputResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] OutputLoop 检测触发: 重复{Count}次, 模式长度={Len}",
                outputResult.RepeatCount, outputResult.RepeatedPattern?.Length ?? 0);
            _journal.OnLoopDetected(
                "OutputLoop", _sessionId, _conversationTurn, _toolCallCount,
                outputResult.LoopTriggerCount,
                $"输出文本循环(重复{outputResult.RepeatCount}次)",
                textSnippet: outputResult.RepeatedPattern);
            return outputResult;
        }

        var fpResult = _logicFingerprintDetector.Record(accumulatedText);
        if (fpResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] LogicFingerprint 检测触发: 指纹={FP}, 命中{Count}次",
                fpResult.Fingerprint, fpResult.HitCount);
            _journal.OnLoopDetected(
                "LogicFingerprint", _sessionId, _conversationTurn, _toolCallCount,
                fpResult.TriggerCount,
                $"逻辑指纹循环(指纹={fpResult.Fingerprint},命中{fpResult.HitCount}次)");
            return new LoopDetectionResult(
                true,
                $"逻辑指纹循环(指纹={fpResult.Fingerprint},命中{fpResult.HitCount}次)",
                fpResult.HitCount,
                0,
                fpResult.TriggerCount);
        }

        return LoopDetectionResult.NoLoop;
    }

    /// <summary>
    /// ILoopDetectionStrategy.CheckTextLoop — 串行漏斗: OutputLoop(廉价)→LogicFingerprint(中等)→ShannonEntropy(昂贵)
    /// 前面触发就不跑后续更昂贵的检测器,降低平均检测成本
    /// </summary>
    public LoopInterventionResult? CheckTextLoop(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        _journal.Record("guardian_check_text", _sessionId, _conversationTurn, _toolCallCount,
            new Dictionary<string, string> { ["text_len"] = text.Length.ToString() });

        var outputResult = _outputLoopDetector.Detect(text);
        if (outputResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] CheckTextLoop: OutputLoop 触发: 重复{Count}次",
                outputResult.RepeatCount);
            _journal.OnLoopDetected(
                "OutputLoop", _sessionId, _conversationTurn, _toolCallCount,
                outputResult.LoopTriggerCount,
                $"输出文本循环(重复{outputResult.RepeatCount}次)",
                textSnippet: outputResult.RepeatedPattern);
            return new LoopInterventionResult(
                outputResult.LoopTriggerCount,
                0,
                $"输出文本循环(重复{outputResult.RepeatCount}次)");
        }

        var fpResult = _logicFingerprintDetector.Record(text);
        if (fpResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] CheckTextLoop: LogicFingerprint 触发: 指纹={FP}, 命中{Count}次",
                fpResult.Fingerprint, fpResult.HitCount);
            _journal.OnLoopDetected(
                "LogicFingerprint", _sessionId, _conversationTurn, _toolCallCount,
                fpResult.TriggerCount,
                $"逻辑指纹循环(命中{fpResult.HitCount}次)",
                textSnippet: text);
            return new LoopInterventionResult(
                fpResult.TriggerCount,
                0,
                $"逻辑指纹循环(命中{fpResult.HitCount}次)");
        }

        var entropyResult = _shannonEntropyDetector.Record(text);
        if (entropyResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] CheckTextLoop: ShannonEntropy 触发: 熵={Entropy:F3}, 连续下降{Streak}轮",
                entropyResult.CurrentEntropy, entropyResult.DeclineStreak);
            _journal.OnLoopDetected(
                "ShannonEntropy", _sessionId, _conversationTurn, _toolCallCount,
                entropyResult.TriggerCount,
                $"信息熵减循环(熵={entropyResult.CurrentEntropy:F3},连续下降{entropyResult.DeclineStreak}轮)",
                entropy: entropyResult.CurrentEntropy,
                textSnippet: text);
            return new LoopInterventionResult(
                entropyResult.TriggerCount,
                0,
                $"信息熵减循环(熵={entropyResult.CurrentEntropy:F3},连续下降{entropyResult.DeclineStreak}轮)");
        }

        return null;
    }

    /// <summary>
    /// ILoopDetectionStrategy.CheckToolCallLoop — 运行 ToolCallSequence 检测
    /// </summary>
    public LoopInterventionResult? CheckToolCallLoop(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        _journal.Record("guardian_check_tool", _sessionId, _conversationTurn, _toolCallCount,
            new Dictionary<string, string> { ["tool_name"] = toolName });

        var argsFingerprint = BuildArgsFingerprint(toolName, arguments);
        var seqResult = _toolCallSequenceDetector.Record(toolName, argsFingerprint);

        if (seqResult.IsLoopDetected)
        {
            _logger?.LogWarning("[InformationEntropyGuardian] CheckToolCallLoop: ToolCallSequence 触发: {Pattern}, 重复{Count}次, 参数匹配={ArgsMatch}",
                seqResult.RepeatedPattern, seqResult.RepeatCount, seqResult.ArgsMatched);
            _journal.OnLoopDetected(
                "ToolCallSequence", _sessionId, _conversationTurn, _toolCallCount,
                seqResult.TriggerCount,
                seqResult.RepeatedPattern ?? "工具调用序列循环");
            return new LoopInterventionResult(
                seqResult.TriggerCount,
                0,
                seqResult.RepeatedPattern ?? "工具调用序列循环");
        }

        return null;
    }

    /// <summary>
    /// 重置所有检测器内部状态
    /// </summary>
    public void Reset()
    {
        _outputLoopDetector.Reset();
        _logicFingerprintDetector.Reset();
        _toolCallSequenceDetector.Reset();
        _shannonEntropyDetector.Reset();
        _journal.Reset();
    }

    /// <summary>
    /// 当前输出循环触发次数
    /// </summary>
    public int LoopTriggerCount => _outputLoopDetector.LoopTriggerCount;

    /// <summary>
    /// 获取诊断日志簿 — 供 DiagnosticLogRecorder 写入 loop_anomaly 条目
    /// </summary>
    public LoopDiagnosticJournal Journal => _journal;

    /// <summary>
    /// 从工具调用参数中提取指纹 — 取关键参数值拼接
    /// 格式: "toolName(key1=val1,key2=val2)"
    /// </summary>
    private static string? BuildArgsFingerprint(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return null;

        var keys = new[] { "file_path", "path", "pattern", "query", "command", "directory", "url", "name", "id" };

        var parts = new List<string>();
        foreach (var key in keys)
        {
            if (arguments.TryGetValue(key, out var value))
            {
                var str = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.GetRawText();
                if (str.Length > 50)
                    str = str[..50] + "...";
                parts.Add($"{key}={str}");
            }
        }

        if (parts.Count == 0)
        {
            foreach (var kvp in arguments.Take(2))
            {
                var str = kvp.Value.ValueKind == JsonValueKind.String
                    ? kvp.Value.GetString() ?? ""
                    : kvp.Value.GetRawText();
                if (str.Length > 50)
                    str = str[..50] + "...";
                parts.Add($"{kvp.Key}={str}");
            }
        }

        return parts.Count > 0 ? $"{toolName}({string.Join(",", parts)})" : null;
    }
}
