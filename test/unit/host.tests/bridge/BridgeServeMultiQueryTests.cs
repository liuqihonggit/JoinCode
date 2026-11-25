namespace Host.Tests.Bridge;

/// <summary>
/// TASK136 — bridge_serve 跨进程多查询（ADR 0165 阶段 2 剩余）：
/// 提交不再因忙被拒，任意时刻可起新查询；事件按 queryId 归因，每查询独立消费器。
/// <para>对照进程内宿主（EngineSession 每查询新建 SessionController + consumer）行为一致。</para>
/// </summary>
public sealed class BridgeServeMultiQueryTests {
    private static string WriteRequest(CliBridge.BridgeRequest request) =>
        JsonSerializer.Serialize(request, CliBridge.BridgeJsonContext.Default.BridgeRequest);

    /// <summary>两条不同 queryId 并发提交：都执行到终态，正文各归其 id 不串台，零 error 行。</summary>
    [Fact]
    public async Task TwoSubmits_Concurrently_BothRunToCompletion() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("第一问") { QueryId = "q-1" }),
            WriteRequest(new CliBridge.BridgeRequest.Submit("第二问") { QueryId = "q-2" })));
        await using var stdout = new StringWriter();

        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        BridgeServeLoop.TurnExecutor executor = async (input, consumer, ct) => {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.TrySetResult();
            consumer.OnText(input);
            consumer.OnDone(null, null);
            // 先起的查询阻塞到「另一条也起来」—— 证明第二条没有被排队/拒绝
            // （守卫故意不挂 ct：EOF 收尾会取消令牌，挂上会让本用例在收尾竞态下假红）
            var finishedFirst = await Task.WhenAny(bothStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (finishedFirst != bothStarted.Task)
                throw new InvalidOperationException("等待另一条查询启动超时（第二条提交被拒绝？）");
        };

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(), executor, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(lines, line => line.Contains("\"type\":\"error\"", StringComparison.Ordinal));
        Assert.Equal(2, lines.Count(line => line.Contains("\"type\":\"turn_started\"", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line.Contains("\"type\":\"turn_completed\"", StringComparison.Ordinal)));

        // 消费器按查询独立 —— 正文不得串到别人的 queryId 上
        var answeredQ1 = lines.Where(line => line.Contains("\"type\":\"assistant_message\"", StringComparison.Ordinal)
                                            && line.Contains("\"queryId\":\"q-1\"", StringComparison.Ordinal)).ToArray();
        var answeredQ2 = lines.Where(line => line.Contains("\"type\":\"assistant_message\"", StringComparison.Ordinal)
                                            && line.Contains("\"queryId\":\"q-2\"", StringComparison.Ordinal)).ToArray();
        Assert.Single(answeredQ1);
        Assert.Single(answeredQ2);
        Assert.Contains("第一问", answeredQ1[0]);
        Assert.Contains("第二问", answeredQ2[0]);
    }

    /// <summary>唯一仍拒绝的情形：同 queryId 并发复用（身份冲突 → 事件无法区分），且不打断在途那条。</summary>
    [Fact]
    public async Task DuplicateQueryId_IsRejected_WithoutTouchingRunningQuery() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("在途") { QueryId = "q-dup" }),
            WriteRequest(new CliBridge.BridgeRequest.Submit("复用身份") { QueryId = "q-dup" })));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.Delay(Timeout.Infinite, ct), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines, line => line.Contains("\"type\":\"turn_started\"", StringComparison.Ordinal));

        var errorLines = lines.Where(line => line.Contains("\"type\":\"error\"", StringComparison.Ordinal)).ToArray();
        Assert.Single(errorLines);
        Assert.Contains("已在执行", errorLines[0]);
        // 诱导：告诉调用方怎么继续（换身份 / 先 interrupt）
        Assert.Contains("interrupt", errorLines[0]);
    }

    /// <summary>定向中断只杀自己：q-slow 被中断，q-fast 正常完成。</summary>
    [Fact]
    public async Task Interrupt_TargetsOnlyMatchingQuery() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("慢") { QueryId = "q-slow" }),
            WriteRequest(new CliBridge.BridgeRequest.Submit("快") { QueryId = "q-fast" }),
            WriteRequest(new CliBridge.BridgeRequest.Interrupt { QueryId = "q-slow" }),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        BridgeServeLoop.TurnExecutor executor = async (input, consumer, ct) => {
            if (input != "快") {
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }
            consumer.OnText("快答");
            consumer.OnDone(null, null);
        };

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(), executor, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var completed = lines.Where(line => line.Contains("\"type\":\"turn_completed\"", StringComparison.Ordinal)).ToArray();
        var interrupted = lines.Where(line => line.Contains("\"type\":\"turn_interrupted\"", StringComparison.Ordinal)).ToArray();

        Assert.Single(completed);
        Assert.Contains("q-fast", completed[0]);
        Assert.Single(interrupted);
        Assert.Contains("q-slow", interrupted[0]);
    }

    /// <summary>退出收尾：quit 取消全部在途查询，各自 turn_interrupted（顺序不承诺）。</summary>
    [Fact]
    public async Task Quit_CancelsAllActiveQueries() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("甲") { QueryId = "q-a" }),
            WriteRequest(new CliBridge.BridgeRequest.Submit("乙") { QueryId = "q-b" }),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.Delay(Timeout.Infinite, ct), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var interrupted = lines.Where(line => line.Contains("\"type\":\"turn_interrupted\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, interrupted.Length);
        Assert.Contains(interrupted, line => line.Contains("\"queryId\":\"q-a\"", StringComparison.Ordinal));
        Assert.Contains(interrupted, line => line.Contains("\"queryId\":\"q-b\"", StringComparison.Ordinal));
    }
}
