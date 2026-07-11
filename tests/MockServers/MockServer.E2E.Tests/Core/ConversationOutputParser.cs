namespace MockServer.E2E.Tests.Core;

public sealed record ToolCallRecord
{
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public bool IsSuccess { get; init; }
    public string Result { get; init; } = "";
}

public sealed record ConversationTurnRecord
{
    public string UserInput { get; init; } = "";
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];
    public string AssistantResponse { get; init; } = "";
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string RawOutput { get; init; } = "";
}

public sealed record AssertResult
{
    public AssertType Type { get; init; }
    public string Expected { get; init; } = "";
    public bool IsPassed { get; init; }
    public string? Description { get; init; }
    public string? ActualValue { get; init; }
}

public sealed record ConversationResult
{
    public string ScriptName { get; init; } = "";
    public IReadOnlyList<ConversationTurnRecord> TurnRecords { get; init; } = [];
    public IReadOnlyList<AssertResult> AssertResults { get; init; } = [];
    public IReadOnlyList<string> DumpFiles { get; init; } = [];
    public PrefixCacheAnalysis? CacheAnalysis { get; init; }
    public string StderrOutput { get; init; } = "";
    public bool AllPassed => AssertResults.All(r => r.IsPassed);
}

public sealed record PrefixCacheAnalysis
{
    public IReadOnlyList<DumpFilePair> AdjacentPairs { get; init; } = [];
    public bool AllPrefixesStable { get; init; }
    public IReadOnlyList<CacheBreakDetail> Breaks { get; init; } = [];
}

public sealed record DumpFilePair
{
    public required string EarlierFile { get; init; }
    public required string LaterFile { get; init; }
    public required int EarlierTurn { get; init; }
    public required int LaterTurn { get; init; }
    public required bool PrefixStable { get; init; }
    public string? BreakReason { get; init; }
}

public sealed record CacheBreakDetail
{
    public required int FromTurn { get; init; }
    public required int ToTurn { get; init; }
    public required string Reason { get; init; }
}

public static class ConversationOutputParser
{
    public static ConversationTurnRecord Parse(string stdoutOutput)
    {
        ArgumentNullException.ThrowIfNull(stdoutOutput);

        var toolCalls = new List<ToolCallRecord>();
        var responseLines = new List<string>();
        var errors = new List<string>();

        ToolCallRecord? currentTool = null;
        var toolArgsBuffer = new StringBuilder();

        foreach (var line in stdoutOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (TryParseToolStart(trimmed, out var toolName, out var toolArgs))
            {
                if (currentTool is not null)
                {
                    currentTool = currentTool with { Arguments = toolArgsBuffer.ToString() };
                    toolCalls.Add(currentTool);
                    toolArgsBuffer.Clear();
                }
                currentTool = new ToolCallRecord { ToolName = toolName };
                toolArgsBuffer.Append(toolArgs);
                continue;
            }

            if (TryParseToolEnd(trimmed, out var endToolName, out var isSuccess))
            {
                if (currentTool is not null)
                {
                    currentTool = currentTool with
                    {
                        Arguments = toolArgsBuffer.ToString(),
                        IsSuccess = isSuccess
                    };
                    toolCalls.Add(currentTool);
                    toolArgsBuffer.Clear();
                    currentTool = null;
                }
                continue;
            }

            if (TryParseToolProgress(trimmed, out var progressToolName, out var progressMsg))
            {
                continue;
            }

            if (TryParseError(trimmed, out var errorMsg))
            {
                errors.Add(errorMsg);
                continue;
            }

            if (currentTool is not null)
            {
                toolArgsBuffer.Append(trimmed);
                continue;
            }

            responseLines.Add(trimmed);
        }

        if (currentTool is not null)
        {
            currentTool = currentTool with { Arguments = toolArgsBuffer.ToString() };
            toolCalls.Add(currentTool);
        }

        return new ConversationTurnRecord
        {
            ToolCalls = toolCalls,
            AssistantResponse = string.Join("\n", responseLines).Trim(),
            Errors = errors,
            RawOutput = stdoutOutput
        };
    }

    public static IReadOnlyList<AssertResult> EvaluateAsserts(
        ConversationTurnRecord record,
        IReadOnlyList<OutputAssert> asserts)
    {
        var results = new List<AssertResult>();

        foreach (var assert in asserts)
        {
            var result = EvaluateSingleAssert(record, assert);
            results.Add(result);
        }

        return results;
    }

    private static AssertResult EvaluateSingleAssert(ConversationTurnRecord record, OutputAssert assert)
    {
        var (isPassed, actualValue) = assert.Type switch
        {
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
                 string.Join(", ", record.ToolCalls.Where(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase)).Select(tc => $"{tc.ToolName}={(tc.IsSuccess ? "OK" : "FAIL")}"))),

            AssertType.ToolCallFailed =>
                (record.ToolCalls.Any(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase) && !tc.IsSuccess),
                 string.Join(", ", record.ToolCalls.Where(tc => tc.ToolName.Contains(assert.Expected, StringComparison.OrdinalIgnoreCase)).Select(tc => $"{tc.ToolName}={(tc.IsSuccess ? "OK" : "FAIL")}"))),

            AssertType.HasAssistantResponse =>
                (!string.IsNullOrWhiteSpace(record.AssistantResponse),
                 record.AssistantResponse),

            AssertType.NoErrors =>
                (record.Errors.Count == 0 && !record.RawOutput.Contains("错误", StringComparison.OrdinalIgnoreCase),
                 record.Errors.Count > 0 ? string.Join("; ", record.Errors) : "(无错误)"),

            AssertType.Custom =>
                (assert.CustomPredicate?.Invoke(record.RawOutput) ?? false,
                 record.RawOutput),

            _ => (false, "未知断言类型")
        };

        return new AssertResult
        {
            Type = assert.Type,
            Expected = assert.Expected,
            IsPassed = isPassed,
            Description = assert.Description,
            ActualValue = actualValue
        };
    }

    private static bool TryParseToolStart(string line, out string toolName, out string toolArgs)
    {
        toolName = "";
        toolArgs = "";

        if (!line.Contains("[Tool]")) return false;

        var toolPrefix = "[Tool] ";
        var idx = line.IndexOf(toolPrefix, StringComparison.Ordinal);
        if (idx < 0) return false;

        var rest = line[(idx + toolPrefix.Length)..];

        var parenIdx = rest.IndexOf('(');
        if (parenIdx >= 0)
        {
            toolName = rest[..parenIdx].Trim();
            var argsEnd = rest.LastIndexOf(')');
            toolArgs = argsEnd > parenIdx
                ? rest[(parenIdx + 1)..argsEnd]
                : rest[(parenIdx + 1)..];
        }
        else
        {
            toolName = rest.Trim();
        }

        return !string.IsNullOrEmpty(toolName);
    }

    private static bool TryParseToolEnd(string line, out string toolName, out bool isSuccess)
    {
        toolName = "";
        isSuccess = false;

        var okPrefix = "[OK] ";
        var failPrefix = "[FAIL] ";

        if (line.Contains(okPrefix))
        {
            var idx = line.IndexOf(okPrefix, StringComparison.Ordinal);
            toolName = line[(idx + okPrefix.Length)..].Trim();
            isSuccess = true;
            return !string.IsNullOrEmpty(toolName);
        }

        if (line.Contains(failPrefix))
        {
            var idx = line.IndexOf(failPrefix, StringComparison.Ordinal);
            toolName = line[(idx + failPrefix.Length)..].Trim();
            isSuccess = false;
            return !string.IsNullOrEmpty(toolName);
        }

        return false;
    }

    private static bool TryParseToolProgress(string line, out string toolName, out string progressMsg)
    {
        toolName = "";
        progressMsg = "";

        if (!line.Contains("[...]")) return false;

        var prefix = "[...] ";
        var idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return false;

        var rest = line[(idx + prefix.Length)..];
        var colonIdx = rest.IndexOf(':');
        if (colonIdx >= 0)
        {
            toolName = rest[..colonIdx].Trim();
            progressMsg = rest[(colonIdx + 1)..].Trim();
        }
        else
        {
            progressMsg = rest.Trim();
        }

        return true;
    }

    private static bool TryParseError(string line, out string errorMsg)
    {
        errorMsg = "";

        if (line.StartsWith("错误:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            errorMsg = line.Trim();
            return true;
        }

        return false;
    }
}
