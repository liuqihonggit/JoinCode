namespace Testing.Common;

/// <summary>
/// flaky 用例外层重试模板（TASK101 B3）: 重试循环集中一处, 用例只表达"什么算通过"。
/// 所有次数耗尽后抛 <see cref="InvalidOperationException"/> 并聚合每次尝试原因, 不吞异常。
/// 配套约定: 使用方的测试必须打 <c>[Trait("Category", "Flaky")]</c>,
/// 让 PR CI 默认排除(过滤 <c>Category!=Flaky</c>), 由夜间专项 job 必跑。
/// </summary>
public static class FlakyRetry {
    /// <summary>默认尝试次数 — 常规偶发(调度抖动)三次足够; 高压场景显式传更大值。</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>
    /// 反复执行 <paramref name="attempt"/> 直到返回 true; 抛出的异常视为本次尝试失败并继续重试
    /// （<see cref="OperationCanceledException"/> 除外, 直接上抛）。
    /// 次数耗尽时抛 <see cref="InvalidOperationException"/>, 消息含 <paramref name="failureMessage"/> 与每次尝试的原因。
    /// </summary>
    public static async Task RunUntilAsync(
        Func<Task<bool>> attempt,
        string failureMessage,
        int maxAttempts = DefaultMaxAttempts) {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        var failures = new List<string>(maxAttempts);
        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++) {
            try {
                if (await attempt()) return;
                failures.Add($"attempt {attemptNumber}: 条件未满足");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                failures.Add($"attempt {attemptNumber}: {ex.GetType().Name}: {ex.Message}");
                if (attemptNumber == maxAttempts) {
                    throw new InvalidOperationException(BuildMessage(failureMessage, maxAttempts, failures), ex);
                }
            }
        }
        throw new InvalidOperationException(BuildMessage(failureMessage, maxAttempts, failures));
    }

    /// <summary>
    /// 无条件重试版本 — <paramref name="attempt"/> 不抛异常即视为通过; 用于"必须不抛"的 flaky 用例。
    /// </summary>
    public static Task RunAsync(Func<Task> attempt, int maxAttempts = DefaultMaxAttempts) {
        ArgumentNullException.ThrowIfNull(attempt);
        return RunUntilAsync(
            async () => {
                await attempt();
                return true;
            },
            "flaky 用例全部尝试均失败",
            maxAttempts);
    }

    private static string BuildMessage(string failureMessage, int maxAttempts, IReadOnlyList<string> failures) =>
        $"{failureMessage} — {maxAttempts} 次尝试全部失败{Environment.NewLine}{string.Join(Environment.NewLine, failures)}";
}
