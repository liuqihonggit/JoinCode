namespace MockServer.E2E.Tests.Core;

public sealed record ToolCallRecord {
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public bool IsSuccess { get; init; }
    public string Result { get; init; } = "";
}

public sealed record ConversationTurnRecord {
    public string UserInput { get; init; } = "";
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];
    public string AssistantResponse { get; init; } = "";
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string RawOutput { get; init; } = "";
}

public sealed record AssertResult {
    public AssertType Type { get; init; }
    public string Expected { get; init; } = "";
    public bool IsPassed { get; init; }
    public string? Description { get; init; }
    public string? ActualValue { get; init; }
}

public sealed record ConversationResult {
    public string ScriptName { get; init; } = "";
    public IReadOnlyList<ConversationTurnRecord> TurnRecords { get; init; } = [];
    public IReadOnlyList<AssertResult> AssertResults { get; init; } = [];
    public IReadOnlyList<string> DumpFiles { get; init; } = [];
    public PrefixCacheAnalysis? CacheAnalysis { get; init; }
    public string StderrOutput { get; init; } = "";
    public bool AllPassed => AssertResults.All(r => r.IsPassed);
}

public sealed record PrefixCacheAnalysis {
    public IReadOnlyList<DumpFilePair> AdjacentPairs { get; init; } = [];
    public bool AllPrefixesStable { get; init; }
    public IReadOnlyList<CacheBreakDetail> Breaks { get; init; } = [];
}

public sealed record DumpFilePair {
    public required string EarlierFile { get; init; }
    public required string LaterFile { get; init; }
    public required int EarlierTurn { get; init; }
    public required int LaterTurn { get; init; }
    public required bool PrefixStable { get; init; }
    public string? BreakReason { get; init; }
}

public sealed record CacheBreakDetail {
    public required int FromTurn { get; init; }
    public required int ToTurn { get; init; }
    public required string Reason { get; init; }
}

public static class ConversationOutputParser {
    public static ConversationTurnRecord Parse(string stdoutOutput) {
        ArgumentNullException.ThrowIfNull(stdoutOutput);

        var toolCalls = new List<ToolCallRecord>();
        var pendingTools = new List<PendingToolCall>();
        var responseLines = new List<string>();
        var errors = new List<string>();

        PendingToolCall? captureTarget = null;
        var captureBuffer = new StringBuilder();

        void FlushCapture() {
            if (captureTarget is null) return;
            var resultText = captureBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(resultText)) {
                captureTarget.Result = string.IsNullOrEmpty(captureTarget.Result) ? resultText : $"{captureTarget.Result}\n{resultText}";
            }
            captureBuffer.Clear();
            captureTarget = null;
        }

        foreach (var line in stdoutOutput.Split('\n')) {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (TryParseToolStart(trimmed, out var toolName, out var toolArgs)) {
                // 并行批次会先集中输出全部 [Tool]，结果稍后按 [OK]/[FAIL] 工具名匹配
                FlushCapture();
                pendingTools.Add(new PendingToolCall { ToolName = toolName, Arguments = toolArgs });
                continue;
            }

            if (TryParseToolEnd(trimmed, out var endToolName, out var isSuccess)) {
                FlushCapture();
                var target = pendingTools.FirstOrDefault(t => t.IsSuccess is null
                    && string.Equals(t.ToolName, endToolName, StringComparison.OrdinalIgnoreCase));
                target ??= pendingTools.LastOrDefault(t => t.IsSuccess is null);
                if (target is not null) {
                    target.IsSuccess = isSuccess;
                    captureTarget = target;
                }
                continue;
            }

            if (captureTarget is not null && trimmed.StartsWith("  ", StringComparison.Ordinal)) {
                captureBuffer.AppendLine(trimmed.TrimStart());
                continue;
            }

            FlushCapture();

            if (TryParseToolProgress(trimmed, out _, out _)) {
                continue;
            }

            if (TryParseError(trimmed, out var errorMsg)) {
                errors.Add(errorMsg);
                continue;
            }

            responseLines.Add(trimmed);
        }

        FlushCapture();

        foreach (var pending in pendingTools) {
            toolCalls.Add(new ToolCallRecord {
                ToolName = pending.ToolName,
                Arguments = pending.Arguments,
                IsSuccess = pending.IsSuccess ?? false,
                Result = pending.Result.Trim()
            });
        }

        return new ConversationTurnRecord {
            ToolCalls = toolCalls,
            AssistantResponse = string.Join("\n", responseLines).Trim(),
            Errors = errors,
            RawOutput = stdoutOutput
        };
    }

    public static IReadOnlyList<AssertResult> EvaluateAsserts(
        ConversationTurnRecord record,
        IReadOnlyList<OutputAssert> asserts) {
        var results = new List<AssertResult>();

        foreach (var assert in asserts) {
            var result = EvaluateSingleAssert(record, assert);
            results.Add(result);
        }

        return results;
    }

    private static AssertResult EvaluateSingleAssert(ConversationTurnRecord record, OutputAssert assert) {
        var (isPassed, actualValue) = assert.Type switch {
            AssertType.ContainsText =>
                (record.RawOutput.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase),
                 record.RawOutput),

            AssertType.NotContainsText =>
                (!record.RawOutput.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase),
                 record.RawOutput),

            AssertType.ContainsToolCall =>
                (record.ToolCalls.Any(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase)),
                 string.Join(", ", record.ToolCalls.Select(tc => tc.ToolName))),

            AssertType.ToolCallSucceeded =>
                (record.ToolCalls.Any(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase) && tc.IsSuccess),
                  string.Join("; ", record.ToolCalls.Where(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase)).Select(tc => $"{tc.ToolName}={(tc.IsSuccess ? "OK" : "FAIL")} result={Truncate(tc.Result, 200)}"))),

            AssertType.ToolCallFailed =>
                (record.ToolCalls.Any(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase) && !tc.IsSuccess),
                  string.Join("; ", record.ToolCalls.Where(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase)).Select(tc => $"{tc.ToolName}={(tc.IsSuccess ? "OK" : "FAIL")} result={Truncate(tc.Result, 200)}"))),

            AssertType.HasAssistantResponse =>
                (!string.IsNullOrWhiteSpace(record.AssistantResponse),
                 record.AssistantResponse),

            AssertType.NoErrors =>
                (record.Errors.Count == 0,
                 record.Errors.Count > 0 ? string.Join("; ", record.Errors) : "(无错误)"),

            AssertType.Custom =>
                (assert.CustomPredicate?.Invoke(record.RawOutput) ?? false,
                 record.RawOutput),

            _ => (false, "未知断言类型")
        };

        return new AssertResult {
            Type = assert.Type,
            Expected = assert.Expected,
            IsPassed = isPassed,
            Description = assert.Description,
            ActualValue = actualValue
        };
    }

    private static bool TryParseToolStart(string line, out string toolName, out string toolArgs) {
        toolName = "";
        toolArgs = "";

        if (!line.Contains("[Tool]")) return false;

        var toolPrefix = "[Tool] ";
        var idx = line.IndexOf(toolPrefix, StringComparison.Ordinal);
        if (idx < 0) return false;

        var rest = line[(idx + toolPrefix.Length)..];

        var parenIdx = rest.IndexOf('(');
        if (parenIdx >= 0) {
            toolName = rest[..parenIdx].Trim();
            var argsEnd = rest.LastIndexOf(')');
            toolArgs = argsEnd > parenIdx
                ? rest[(parenIdx + 1)..argsEnd]
                : rest[(parenIdx + 1)..];
        } else {
            toolName = rest.Trim();
        }

        return !string.IsNullOrEmpty(toolName);
    }

    private static bool TryParseToolEnd(string line, out string toolName, out bool isSuccess) {
        toolName = "";
        isSuccess = false;

        var okPrefix = "[OK] ";
        var failPrefix = "[FAIL] ";

        if (line.Contains(okPrefix)) {
            var idx = line.IndexOf(okPrefix, StringComparison.Ordinal);
            toolName = line[(idx + okPrefix.Length)..].Trim();
            isSuccess = true;
            return !string.IsNullOrEmpty(toolName);
        }

        if (line.Contains(failPrefix)) {
            var idx = line.IndexOf(failPrefix, StringComparison.Ordinal);
            toolName = line[(idx + failPrefix.Length)..].Trim();
            isSuccess = false;
            return !string.IsNullOrEmpty(toolName);
        }

        return false;
    }

    private static bool TryParseToolProgress(string line, out string toolName, out string progressMsg) {
        toolName = "";
        progressMsg = "";

        if (!line.Contains("[...]")) return false;

        var prefix = "[...] ";
        var idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return false;

        var rest = line[(idx + prefix.Length)..];
        var colonIdx = rest.IndexOf(':');
        if (colonIdx >= 0) {
            toolName = rest[..colonIdx].Trim();
            progressMsg = rest[(colonIdx + 1)..].Trim();
        } else {
            progressMsg = rest.Trim();
        }

        return true;
    }

    private static bool TryParseError(string line, out string errorMsg) {
        errorMsg = "";

        if (IsDotNetILoggerLine(line))
            return false;

        if (line.StartsWith("错误:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Exception", StringComparison.OrdinalIgnoreCase)) {
            errorMsg = line.Trim();
            return true;
        }

        return false;
    }

    private static readonly string[] ILoggerPrefixes = ["crit:", "fail:", "error:", "warn:", "info:", "dbug:", "trce:"];

    /// <summary>
    /// 检测 .NET ILogger simple console 格式的日志行
    /// 格式特征: "level: Namespace.Class[EventId]" — 冒号后紧跟带点号和方括号的类路径
    /// 真正的错误行: "Error: something went wrong" — 冒号后是自然语言，不含 [EventId] 模式
    /// </summary>
    private static bool IsDotNetILoggerLine(string line) {
        // 去掉 jcc AI 回复前缀 "> " 或 "> > " 后再检测
        var stripped = line.AsSpan().TrimStart();
        while (stripped.Length > 2 && stripped[0] == '>' && stripped[1] == ' ')
            stripped = stripped[2..].TrimStart();
        var normalized = stripped.ToString();

        foreach (var prefix in ILoggerPrefixes) {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                var rest = normalized[prefix.Length..].TrimStart();
                if (rest.Contains('[', StringComparison.Ordinal) &&
                    rest.Contains(']', StringComparison.Ordinal) &&
                    rest.IndexOf('[') > 0)
                    return true;
            }
        }

        return false;
    }

    private static string Truncate(string s, int maxLen) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= maxLen ? s : s[..maxLen] + "...";

    /// <summary>
    /// 尚未收到 [OK]/[FAIL] 的工具调用 — 并行批次允许先集中输出全部 [Tool] 行。
    /// </summary>
    private sealed class PendingToolCall {
        public string ToolName { get; init; } = "";
        public string Arguments { get; init; } = "";
        public string Result { get; set; } = "";
        public bool? IsSuccess { get; set; }
    }
}