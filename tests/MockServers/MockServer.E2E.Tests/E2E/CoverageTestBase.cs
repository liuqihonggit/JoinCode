using System.Diagnostics;

namespace MockServer.E2E.Tests;

/// <summary>
/// E2E 覆盖测试基类 — 提供共享的脚本运行、日志、断言辅助方法
/// 拆分自原 CoverageExpansionTests，供 10+ 个独立测试类继承以启用 xUnit 集合并行
/// </summary>
[Trait("Category", "Integration")]
public abstract class CoverageTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly ILoggerFactory LoggerFactory;

    protected CoverageTestBase(ITestOutputHelper output)
    {
        Output = output;
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        LoggerFactory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 运行对话脚本并验证所有断言通过
    /// 全局超时 60s，防止 jcc.exe 卡死导致整个测试套件挂起
    /// 偶发性失败自动重试5次（E2E测试受CI环境资源竞争影响）
    /// </summary>
    protected async Task RunScriptAsync(ConversationScript script, ProviderKind provider = ProviderKind.OpenAI)
    {
        await RunScriptWithRetryAsync(script, provider, maxAttempts: 5).ConfigureAwait(true);
    }

    private async Task RunScriptWithRetryAsync(ConversationScript script, ProviderKind provider, int maxAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sw = Stopwatch.StartNew();
            var runner = new DualRoleConversationRunner(
                LoggerFactory.CreateLogger<DualRoleConversationRunner>());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                var result = await runner.RunAsync(script, provider, timeoutCts.Token).ConfigureAwait(true);
                sw.Stop();

                LogResult(result, sw.Elapsed);
                LogComponentCoverage(result);

                if (result.AllPassed)
                    return;

                if (attempt < maxAttempts)
                {
                    Output.WriteLine($"[Coverage] ⚠ 第{attempt}次尝试失败，自动重试: {script.Name} (provider={provider})");
                    continue;
                }

                result.AllPassed.Should().BeTrue(
                    $"所有断言应通过。失败: {FormatFailures(result)}");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                sw.Stop();
                if (attempt < maxAttempts)
                {
                    Output.WriteLine($"[Coverage] ⚠ 第{attempt}次尝试超时(>60s)，自动重试: {script.Name} (provider={provider})");
                    continue;
                }
                throw new TimeoutException($"[GEN035] 测试超时(>60s): {script.Name} (provider={provider})");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("GEN019", StringComparison.Ordinal) && attempt < maxAttempts)
            {
                sw.Stop();
                Output.WriteLine($"[Coverage] ⚠ 第{attempt}次尝试进程退出无输出(GEN019)，自动重试: {script.Name} (provider={provider})");
                continue;
            }
            finally
            {
                await runner.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// 从 stderr 输出中解析 [STEP] 和 [Timing] 行，记录组件 ✓ 验证和计时
    /// </summary>
    private void LogComponentCoverage(ConversationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.StderrOutput))
        {
            Output.WriteLine("[Coverage] ⚠ 无 stderr 输出，无法进行组件验证");
            return;
        }

        var steps = new List<string>();
        var timings = new List<string>();

        foreach (var line in result.StderrOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Contains("[STEP]", StringComparison.Ordinal))
            {
                steps.Add(trimmed);
            }
            if (trimmed.Contains("[Timing]", StringComparison.Ordinal))
            {
                timings.Add(trimmed);
            }
        }

        if (steps.Count > 0)
        {
            Output.WriteLine($"[Coverage] ✅ 组件验证 ({steps.Count} 个组件):");
            foreach (var step in steps.OrderBy(s => s))
            {
                Output.WriteLine($"  ✓ {step.Trim()}");
            }
        }
        else
        {
            Output.WriteLine("[Coverage] ⚠ 未发现 [STEP] 组件标记");
        }

        if (timings.Count > 0)
        {
            Output.WriteLine($"[Coverage] ⏱ 计时记录 ({timings.Count} 条):");
            foreach (var timing in timings.OrderBy(t => t))
            {
                Output.WriteLine($"  ⏱ {timing.Trim()}");
            }
        }
    }

    private void LogResult(ConversationResult result, TimeSpan elapsed)
    {
        Output.WriteLine($"[Coverage] 脚本: {result.ScriptName}");
        Output.WriteLine($"[Coverage] 轮次数: {result.TurnRecords.Count}");
        Output.WriteLine($"[Coverage] 耗时: {elapsed.TotalMilliseconds:F1}ms");
        Output.WriteLine($"[Coverage] 断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败");

        var hasFailures = result.AssertResults.Any(a => !a.IsPassed);

        foreach (var turn in result.TurnRecords)
        {
            Output.WriteLine($"--- Turn: UserInput=\"{turn.UserInput}\"");
            Output.WriteLine($"    ToolCalls: {turn.ToolCalls.Count}");

            foreach (var tc in turn.ToolCalls)
            {
                var status = tc.IsSuccess ? "OK" : "FAIL";
                var resultPreview = tc.Result.Length > 200 ? tc.Result[..200] + "..." : tc.Result;
                Output.WriteLine($"    [{status}] {tc.ToolName}: {resultPreview}");
            }

            var respPreview = turn.AssistantResponse.Length > 100
                ? turn.AssistantResponse[..100] + "..."
                : turn.AssistantResponse;
            Output.WriteLine($"    AssistantResponse: {respPreview}");
            Output.WriteLine($"    Errors: {turn.Errors.Count}");

            if (turn.Errors.Count > 0)
            {
                foreach (var err in turn.Errors.Take(10))
                {
                    var errPreview = err.Length > 300 ? err[..300] + "..." : err;
                    Output.WriteLine($"    ERROR: {errPreview}");
                }
            }
        }

        foreach (var assert in result.AssertResults.Where(a => !a.IsPassed))
        {
            var actual = string.IsNullOrWhiteSpace(assert.ActualValue) ? "" : $" Actual=\"{Truncate(assert.ActualValue, 200)}\"";
            Output.WriteLine($"FAIL: {assert.Type} Expected=\"{assert.Expected}\"{actual} Desc=\"{assert.Description}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.StderrOutput))
        {
            var stderrLines = result.StderrOutput.Split('\n')
                .Where(l => l.Contains("DIAG") || l.Contains("StreamingFallback") || l.Contains("Enabled"))
                .Take(20).ToList();
            if (stderrLines.Count > 0)
            {
                Output.WriteLine($"[Coverage] DIAG stderr:");
                foreach (var line in stderrLines)
                    Output.WriteLine($"  {line.TrimEnd('\r')}");
            }
        }

        if (hasFailures)
        {
            Output.WriteLine("[Coverage] === 诊断: RawOutput 片段 ===");
            foreach (var turn in result.TurnRecords)
            {
                if (turn.RawOutput.Length > 0)
                {
                    var rawPreview = turn.RawOutput.Length > 2000 ? turn.RawOutput[..2000] + "..." : turn.RawOutput;
                    Output.WriteLine($"--- Turn RawOutput (len={turn.RawOutput.Length}):");
                    Output.WriteLine(rawPreview);
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StderrOutput))
            {
                var stderrPreview = result.StderrOutput.Length > 2000 ? result.StderrOutput[..2000] + "..." : result.StderrOutput;
                Output.WriteLine($"--- StderrOutput (len={result.StderrOutput.Length}):");
                Output.WriteLine(stderrPreview);
            }
        }
    }

    private static string Truncate(string s, int maxLen) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= maxLen ? s : s[..maxLen] + "...";

    private static string FormatFailures(ConversationResult result)
    {
        var failures = result.AssertResults.Where(a => !a.IsPassed).ToList();
        if (failures.Count == 0) return "(无)";
        return string.Join("; ", failures.Select(f =>
        {
            var actual = string.IsNullOrWhiteSpace(f.ActualValue) ? "" : $", Actual=\"{Truncate(f.ActualValue, 100)}\"";
            return $"{f.Type}: Expected=\"{f.Expected}\"{actual} Desc=\"{f.Description}\"";
        }));
    }
}
