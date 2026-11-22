namespace LockDiagnosis.Tests;

/// <summary>
/// CasRetry 活锁防御测试 — 验证 CAS 重试循环的终止性与上限防御。
/// 背景: 变异测试曾将 CAS 循环的 break 条件反转导致 testhost 无限自旋挂死,
/// CasRetry 用 SpinWait.Count 上限将任何无限重试转换为快速失败异常。
/// </summary>
public sealed class CasRetryTests {
    private sealed class Box {
        public required int Value { get; init; }
    }

    [Fact]
    public void Spin_UpdatesValueAtomically() {
        var box = new Box { Value = 1 };

        CasRetry.Spin(ref box, current => new Box { Value = current.Value + 1 });

        Assert.Equal(2, box.Value);
    }

    [Fact]
    public void Spin_NoOpShortCircuitsWithoutCas() {
        var box = new Box { Value = 42 };
        var updateCalls = 0;
        var stable = box;

        CasRetry.Spin(ref box, current => {
            updateCalls++;
            return current;
        });

        Assert.Equal(1, updateCalls);
        Assert.Same(stable, box);
    }

    [Fact]
    public async Task Spin_TerminatesUnderHighContention() {
        const int Threads = 16;
        const int Iterations = 500;
        var box = new Box { Value = 0 };
        using var countdown = new System.Threading.CountdownEvent(Threads);
        var workers = new Task[Threads];

        for (var t = 0; t < Threads; t++) {
            workers[t] = Task.Run(() => {
                countdown.Signal();
                countdown.Wait();
                for (var i = 0; i < Iterations; i++) {
                    CasRetry.Spin(ref box, current => new Box { Value = current.Value + 1 });
                }
            });
        }

        await Task.WhenAll(workers);

        Assert.Equal(Threads * Iterations, box.Value);
    }

    [Fact]
    public async Task Spin_ConcurrentMapUpdatesLoseNoKeys() {
        const int Threads = 8;
        const int KeysPerThread = 200;
        var map = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
        using var countdown = new System.Threading.CountdownEvent(Threads);
        var workers = new Task[Threads];

        for (var t = 0; t < Threads; t++) {
            var threadId = t;
            workers[t] = Task.Run(() => {
                countdown.Signal();
                countdown.Wait();
                CasRetry.Spin(ref map, current => {
                    var updated = current;
                    for (var k = 0; k < KeysPerThread; k++) {
                        updated = updated.SetItem("t" + threadId + "-k" + k, threadId * 1000 + k);
                    }
                    return updated;
                });
            });
        }

        await Task.WhenAll(workers);

        Assert.Equal(Threads * KeysPerThread, map.Count);
    }
}
