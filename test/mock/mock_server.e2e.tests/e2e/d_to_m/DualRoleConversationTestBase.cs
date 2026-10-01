namespace MockServer.E2E.Tests;

[Trait("Category", "Integration")]
public abstract class DualRoleConversationTestBase : IAsyncLifetime {
    protected readonly ITestOutputHelper Output;
    protected readonly ILoggerFactory LoggerFactory;

    /// <summary>重试间隔 — 固定1s,16次共16s,防CI雪崩</summary>
    protected static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    protected DualRoleConversationTestBase(ITestOutputHelper output) {
        Output = output;
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() {
        LoggerFactory.Dispose();
        return Task.CompletedTask;
    }

    protected async Task RunScriptAsync(ConversationScript script) {
        await RunScriptWithRetryAsync(script).ConfigureAwait(true);
    }

    private async Task RunScriptWithRetryAsync(ConversationScript script, int maxAttempts = 16) {
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            var runner = new DualRoleConversationRunner(
                LoggerFactory.CreateLogger<DualRoleConversationRunner>());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try {
                var result = await runner.RunAsync(script, VendorKind.OpenAi, timeoutCts.Token).ConfigureAwait(true);

                LogResult(result);

                if (result.AllPassed)
                    return;

                if (attempt < maxAttempts) {
                    Output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试失败，自动重试: {script.Name}");
                    await Task.Delay(RetryInterval).ConfigureAwait(true);
                    continue;
                }

                result.AllPassed.Should().BeTrue($"所有断言应通过。失败: {FormatFailures(result)}");
            } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                if (attempt < maxAttempts) {
                    Output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试超时(>60s)，自动重试: {script.Name}");
                    await Task.Delay(RetryInterval).ConfigureAwait(true);
                    continue;
                }
                throw new TimeoutException($"[GEN036] 测试超时(>60s): {script.Name}");
            } finally {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    protected async Task<ConversationResult> RunScriptWithCacheAnalysisAsync(ConversationScript script) {
        return await RunScriptWithCacheAnalysisRetryAsync(script).ConfigureAwait(true);
    }

    private async Task<ConversationResult> RunScriptWithCacheAnalysisRetryAsync(ConversationScript script, int maxAttempts = 16) {
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            var runner = new DualRoleConversationRunner(
                LoggerFactory.CreateLogger<DualRoleConversationRunner>());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try {
                var result = await runner.RunAsync(script, VendorKind.OpenAi, timeoutCts.Token).ConfigureAwait(true);

                LogResult(result);

                if (result.AllPassed)
                    return result;

                if (attempt < maxAttempts) {
                    Output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试失败，自动重试: {script.Name}");
                    await Task.Delay(RetryInterval).ConfigureAwait(true);
                    continue;
                }

                result.AllPassed.Should().BeTrue($"所有断言应通过。失败: {FormatFailures(result)}");
                return result;
            } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                if (attempt < maxAttempts) {
                    Output.WriteLine($"[DualRole] ⚠ 第{attempt}次尝试超时(>60s)，自动重试: {script.Name}");
                    await Task.Delay(RetryInterval).ConfigureAwait(true);
                    continue;
                }
                throw new TimeoutException($"[GEN037] 测试超时(>60s): {script.Name}");
            } finally {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }

        throw new InvalidOperationException("[GEN038] [E2E010] 不应到达此处");
    }

    private void LogResult(ConversationResult result) {
        Output.WriteLine($"脚本: {result.ScriptName}");
        Output.WriteLine($"轮次数: {result.TurnRecords.Count}");
        Output.WriteLine($"断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败");

        var hasFailures = result.AssertResults.Any(a => !a.IsPassed);

        foreach (var turn in result.TurnRecords) {
            Output.WriteLine($"--- Turn: UserInput=\"{turn.UserInput}\"");
            Output.WriteLine($"    ToolCalls: {turn.ToolCalls.Count}");

            foreach (var tc in turn.ToolCalls) {
                var status = tc.IsSuccess ? "OK" : "FAIL";
                var resultPreview = tc.Result.Length > 200 ? tc.Result[..200] + "..." : tc.Result;
                Output.WriteLine($"    [{status}] {tc.ToolName}: {resultPreview}");
            }

            Output.WriteLine($"    AssistantResponse: {turn.AssistantResponse[..Math.Min(100, turn.AssistantResponse.Length)]}...");
            Output.WriteLine($"    Errors: {turn.Errors.Count}");

            if (turn.Errors.Count > 0) {
                foreach (var err in turn.Errors.Take(10)) {
                    var errPreview = err.Length > 300 ? err[..300] + "..." : err;
                    Output.WriteLine($"    ERROR: {errPreview}");
                }
            }
        }

        foreach (var assert in result.AssertResults.Where(a => !a.IsPassed)) {
            var actual = string.IsNullOrWhiteSpace(assert.ActualValue) ? "" : $" Actual=\"{Truncate(assert.ActualValue, 200)}\"";
            Output.WriteLine($"FAIL: {assert.Type} Expected=\"{assert.Expected}\"{actual} Desc=\"{assert.Description}\"");
        }

        if (hasFailures) {
            Output.WriteLine("[DualRole] === 诊断: RawOutput 片段 ===");
            foreach (var turn in result.TurnRecords) {
                if (turn.RawOutput.Length > 0) {
                    var rawPreview = turn.RawOutput.Length > 2000 ? turn.RawOutput[..2000] + "..." : turn.RawOutput;
                    Output.WriteLine($"--- Turn RawOutput (len={turn.RawOutput.Length}):");
                    Output.WriteLine(rawPreview);
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StderrOutput)) {
                var stderrPreview = result.StderrOutput.Length > 2000 ? result.StderrOutput[..2000] + "..." : result.StderrOutput;
                Output.WriteLine($"--- StderrOutput (len={result.StderrOutput.Length}):");
                Output.WriteLine(stderrPreview);
            }
        }
    }

    private static string Truncate(string s, int maxLen) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= maxLen ? s : s[..maxLen] + "...";

    private static string FormatFailures(ConversationResult result) {
        var failures = result.AssertResults.Where(a => !a.IsPassed).ToList();
        if (failures.Count == 0) return "(无)";
        return string.Join("; ", failures.Select(f => {
            var actual = string.IsNullOrWhiteSpace(f.ActualValue) ? "" : $", Actual=\"{Truncate(f.ActualValue, 100)}\"";
            return $"{f.Type}: Expected=\"{f.Expected}\"{actual} Desc=\"{f.Description}\"";
        }));
    }

    protected static string FormatCacheBreaks(PrefixCacheAnalysis analysis) {
        if (analysis.Breaks.Count == 0) return "(无)";
        return string.Join("; ", analysis.Breaks.Select(b =>
            $"Turn{b.FromTurn}->Turn{b.ToTurn}: {b.Reason}"));
    }
}
