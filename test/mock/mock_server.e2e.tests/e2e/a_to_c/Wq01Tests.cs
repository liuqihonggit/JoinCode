namespace MockServer.E2E.Tests;

/// <summary>
/// WQ 组限流 E2E 测试 — C2「低限额进程级验证」（TASK131 P1-1 / TASK130 C2）。
/// <para>
/// 与脚本层（<see cref="Scripts.WqQueryRateLimitScripts"/>）配合：脚本断言第 2 回合报限流、
/// 拿不到模型回复；本测试类再向 MockServer 控制面（<c>GET /__control__/requests</c>）核对
/// 请求计数增量 —— 两次提交只允许第 1 次真实进入 MockServer。
/// </para>
/// <para>
/// 重试语义：本测试自带按次重试循环（每次尝试都是全新 jcc 进程，限流计数器随之重置），
/// 因此请求计数按「单次尝试的增量」判定（基线在每次尝试前重新采样），避免重试累积污染计数。
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection("MockServerShared")]
[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = "E2E 限流测试需写入临时 profile 目录（JCC_PROFILE_DIR 外部覆盖）并清理")]
public sealed class Wq01Tests : CoverageTestBase {
    /// <summary>单次尝试上限 — 与 CoverageTestBase 的 flaky 重试口径一致，但按次采样请求计数。</summary>
    private const int MaxAttempts = 4;

    public Wq01Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task Wq01() {
        Output.WriteLine("[Wq] wq01 C2 低限额第二次查询被拦且不进 MockServer");
        var profileDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jcc_c2_profile_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(profileDir);
        var c2Yaml = """
            inherits: base
            overrides:
              - consumer: query-pipeline
                node: query-rate-limit-mw
                config:
                  max-requests: "1"
                  window-seconds: "60"
            """;
        await IO.FileSystem.SafeFileIO.WriteAllText(System.IO.Path.Combine(profileDir, "c2.yaml"), c2Yaml).ConfigureAwait(true);
        Output.WriteLine($"[Wq] 外部 profile 目录: {profileDir}");

        try {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
                var baseline = await GetMockServerRequestCountAsync().ConfigureAwait(true);
                Output.WriteLine($"[Wq] 第{attempt}次尝试 · MockServer 请求基线: {baseline}");

                var runner = new DualRoleConversationRunner(
                    LoggerFactory.CreateLogger<DualRoleConversationRunner>(), sharedFixture: SharedFixture);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                ConversationResult result;
                try {
                    result = await runner.RunAsync(
                        Scripts.WqQueryRateLimitScripts.Wq01LowLimitSecondTurnBlocked(profileDir),
                        VendorKind.OpenAi, timeoutCts.Token).ConfigureAwait(true);
                } catch (Exception ex) when (attempt < MaxAttempts && IsRetryable(ex)) {
                    Output.WriteLine($"[Wq] ⚠ 第{attempt}次尝试进程级失败，重试: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
                    continue;
                } finally {
                    await runner.DisposeAsync().ConfigureAwait(true);
                }

                var delta = await GetMockServerRequestCountAsync().ConfigureAwait(true) - baseline;
                Output.WriteLine($"[Wq] 断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败 · MockServer 请求增量: {delta}");

                if (result.AllPassed) {
                    delta.Should().Be(1,
                        "单次尝试内两次提交：第 1 回合恰好 1 个请求进入 MockServer，第 2 回合必须在限流层被拦（0 请求）");
                    return;
                }

                if (attempt < MaxAttempts) {
                    Output.WriteLine($"[Wq] ⚠ 第{attempt}次尝试断言未全过，重试: {Scripts.WqQueryRateLimitScripts.Wq01LowLimitSecondTurnBlocked(profileDir).Name}");
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
                    continue;
                }

                result.AllPassed.Should().BeTrue(
                    $"所有断言应通过。失败: {string.Join("; ", result.AssertResults.Where(a => !a.IsPassed)
                        .Select(f => $"{f.Type}: Expected=\"{f.Expected}\" Desc=\"{f.Description}\""))}");
            }
        } finally {
            TryDeleteProfileDir(profileDir);
        }
    }

    /// <summary>读 MockServer 控制面请求计数 — <c>GET /__control__/requests</c> 返回已捕获请求列表。</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试代码解析控制面响应")]
    private async Task<int> GetMockServerRequestCountAsync() {
        ArgumentNullException.ThrowIfNull(SharedFixture);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await http.GetStringAsync($"{SharedFixture.BaseUrl}/__control__/requests").ConfigureAwait(true);
        using var doc = System.Text.Json.JsonDocument.Parse(response);
        return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? doc.RootElement.GetArrayLength()
            : 0;
    }

    private static bool IsRetryable(Exception ex) =>
        ex is TimeoutException
        || (ex is InvalidOperationException io && (io.Message.StartsWith("[GEN019]", StringComparison.Ordinal)
            || io.Message.StartsWith("[GEN027]", StringComparison.Ordinal)
            || io.Message.StartsWith("[GEN028]", StringComparison.Ordinal)
            || io.Message.StartsWith("[GEN030]", StringComparison.Ordinal)));

    private void TryDeleteProfileDir(string profileDir) {
        try {
            if (System.IO.Directory.Exists(profileDir))
                System.IO.Directory.Delete(profileDir, recursive: true);
        } catch (Exception ex) {
            Output.WriteLine($"[Wq] ⚠ 清理临时 profile 目录失败（可忽略）: {ex.Message}");
        }
    }
}
