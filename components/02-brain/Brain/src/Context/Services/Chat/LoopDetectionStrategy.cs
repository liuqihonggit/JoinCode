namespace Core.Context;

/// <summary>
/// 循环干预结果 — QueryLoop 中检测到循环时返回，触发 LoopDetected 事件
/// 与 Loop/LoopDetectionResult（输出循环检测）语义不同，此类型用于工具调用序列与逻辑指纹循环
/// </summary>
public sealed record LoopInterventionResult(int TriggerCount, int ToolCallCount, string Reason);

/// <summary>
/// 循环检测策略接口 — 文本循环检测和工具调用序列循环检测
/// </summary>
public interface ILoopDetectionStrategy
{
    /// <summary>
    /// 检测文本响应的逻辑指纹循环
    /// </summary>
    LoopInterventionResult? CheckTextLoop(string text);

    /// <summary>
    /// 检测工具调用序列循环
    /// </summary>
    LoopInterventionResult? CheckToolCallLoop(string toolName, Dictionary<string, JsonElement>? arguments);
}

/// <summary>
/// 组合循环检测策略 — 封装逻辑指纹检测和工具调用序列检测
/// </summary>
public sealed class CompositeLoopDetectionStrategy : ILoopDetectionStrategy
{
    private readonly LogicFingerprintDetector _logicFingerprintDetector = new();
    private readonly ToolCallSequenceDetector _toolSequenceDetector = new();
    private readonly ILogger? _logger;

    public CompositeLoopDetectionStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public LoopInterventionResult? CheckTextLoop(string text)
    {
        var fpResult = _logicFingerprintDetector.Record(text);
        if (fpResult.IsLoopDetected)
        {
            _logger?.LogWarning("[LoopDetectionStrategy] 检测到逻辑指纹循环: 指纹={FP}，命中{Count}次",
                fpResult.Fingerprint, fpResult.HitCount);
            return new LoopInterventionResult(
                fpResult.TriggerCount,
                0,
                $"逻辑指纹循环(命中{fpResult.HitCount}次)");
        }
        return null;
    }

    /// <inheritdoc/>
    public LoopInterventionResult? CheckToolCallLoop(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        var argsFingerprint = BuildArgsFingerprint(toolName, arguments);
        var seqResult = _toolSequenceDetector.Record(toolName, argsFingerprint);
        if (seqResult.IsLoopDetected)
        {
            _logger?.LogWarning("[LoopDetectionStrategy] 检测到工具调用序列循环: {Pattern}，重复{Count}次，参数匹配={ArgsMatch}",
                seqResult.RepeatedPattern, seqResult.RepeatCount, seqResult.ArgsMatched);
            return new LoopInterventionResult(
                seqResult.TriggerCount,
                0,
                seqResult.RepeatedPattern ?? "工具调用序列循环");
        }
        return null;
    }

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
