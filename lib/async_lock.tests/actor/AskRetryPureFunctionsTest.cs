namespace Core.Utils;

/// <summary>
/// AskWithRetryAsync 纯函数确定性单元测试 — 验证退避计算、总超时计算、幂等判断,不依赖时序/不启动 Actor。
/// <para>覆盖:退避延迟边界(0/1/10/负数/上限钳制)、总超时计算、幂等命令判断(IIdempotent/null/普通)。</para>
/// <para>static 方法通过具体化泛型(PureFuncActor)调用,不实例化 Actor,无 Consumer 线程,无时序依赖。</para>
/// </summary>
public class AskRetryPureFunctionsTest {

    #region ComputeBackoffDelayMs — 指数退避延迟纯函数

    /// <summary>attempt=0 → 100ms(首次退避,最小值)</summary>
    [Fact]
    public void Backoff_Attempt0_Returns100() {
        PureFuncActor.ComputeBackoffDelayMs(0).Should().Be(100);
    }

    /// <summary>attempt=1 → 200ms(指数增长 100×2^1)</summary>
    [Fact]
    public void Backoff_Attempt1_Returns200() {
        PureFuncActor.ComputeBackoffDelayMs(1).Should().Be(200);
    }

    /// <summary>attempt=2 → 400ms(100×2^2)</summary>
    [Fact]
    public void Backoff_Attempt2_Returns400() {
        PureFuncActor.ComputeBackoffDelayMs(2).Should().Be(400);
    }

    /// <summary>attempt=3 → 800ms(100×2^3)</summary>
    [Fact]
    public void Backoff_Attempt3_Returns800() {
        PureFuncActor.ComputeBackoffDelayMs(3).Should().Be(800);
    }

    /// <summary>attempt=4 → 1600ms(100×2^4)</summary>
    [Fact]
    public void Backoff_Attempt4_Returns1600() {
        PureFuncActor.ComputeBackoffDelayMs(4).Should().Be(1600);
    }

    /// <summary>attempt=5 → 3200ms(100×2^5,未触上限)</summary>
    [Fact]
    public void Backoff_Attempt5_Returns3200() {
        PureFuncActor.ComputeBackoffDelayMs(5).Should().Be(3200);
    }

    /// <summary>attempt=6 → 5000ms(100×2^6=6400,触上限5000钳制)</summary>
    [Fact]
    public void Backoff_Attempt6_CappedAt5000() {
        PureFuncActor.ComputeBackoffDelayMs(6).Should().Be(5000);
    }

    /// <summary>attempt=10 → 5000ms(100×2^10=102400,钳制为10后触上限)</summary>
    [Fact]
    public void Backoff_Attempt10_CappedAt5000() {
        PureFuncActor.ComputeBackoffDelayMs(10).Should().Be(5000);
    }

    /// <summary>attempt=20 → 5000ms(超过10钳制为10,再触上限)</summary>
    [Fact]
    public void Backoff_Attempt20_ClampedAndCapped() {
        PureFuncActor.ComputeBackoffDelayMs(20).Should().Be(5000);
    }

    /// <summary>attempt=100 → 5000ms(远超上限,钳制+上限双重保护)</summary>
    [Fact]
    public void Backoff_Attempt100_ClampedAndCapped() {
        PureFuncActor.ComputeBackoffDelayMs(100).Should().Be(5000);
    }

    /// <summary>attempt=-1 → 100ms(负数守卫,返回最小退避)</summary>
    [Fact]
    public void Backoff_NegativeAttempt_ReturnsMinBackoff() {
        PureFuncActor.ComputeBackoffDelayMs(-1).Should().Be(100);
    }

    /// <summary>attempt=int.MinValue → 100ms(极端负数守卫)</summary>
    [Fact]
    public void Backoff_MinInt_ReturnsMinBackoff() {
        PureFuncActor.ComputeBackoffDelayMs(int.MinValue).Should().Be(100);
    }

    /// <summary>退避序列单调递增(直到上限)— 0→5 递增,6+ 均为上限</summary>
    [Fact]
    public void Backoff_SequenceMonotonicUntilCap() {
        var prev = 0;
        for (var attempt = 0; attempt <= 5; attempt++) {
            var current = PureFuncActor.ComputeBackoffDelayMs(attempt);
            current.Should().BeGreaterThan(prev, $"attempt={attempt} 应大于 attempt={attempt - 1}");
            prev = current;
        }
    }

    /// <summary>上限后保持恒定 — attempt 6~20 均为 5000</summary>
    [Fact]
    public void Backoff_AfterCap_StaysConstant() {
        for (var attempt = 6; attempt <= 20; attempt++) {
            PureFuncActor.ComputeBackoffDelayMs(attempt).Should().Be(5000, $"attempt={attempt} 应为上限5000");
        }
    }

    #endregion

    #region ComputeTotalTimeoutMs — 总超时纯函数

    /// <summary>默认配置 (10000ms, 16次) → 170000ms</summary>
    [Fact]
    public void TotalTimeout_DefaultConfig_Returns170000() {
        PureFuncActor.ComputeTotalTimeoutMs(10_000, 16).Should().Be(170_000);
    }

    /// <summary>singleTimeoutMs=0 → 0(无超时)</summary>
    [Fact]
    public void TotalTimeout_ZeroSingle_ReturnsZero() {
        PureFuncActor.ComputeTotalTimeoutMs(0, 16).Should().Be(0);
    }

    /// <summary>maxRetries=0 → singleTimeoutMs(仅1次尝试)</summary>
    [Fact]
    public void TotalTimeout_ZeroRetries_ReturnsSingle() {
        PureFuncActor.ComputeTotalTimeoutMs(100, 0).Should().Be(100);
    }

    /// <summary>(5000, 3) → 20000(4次尝试×5000ms)</summary>
    [Fact]
    public void TotalTimeout_FiveThousandThree_Returns20000() {
        PureFuncActor.ComputeTotalTimeoutMs(5000, 3).Should().Be(20_000);
    }

    /// <summary>线性关系 — (single, max) = single × (max+1)</summary>
    [Fact]
    public void TotalTimeout_LinearRelationship() {
        for (var max = 0; max <= 16; max++) {
            PureFuncActor.ComputeTotalTimeoutMs(1000, max).Should().Be(1000 * (max + 1));
        }
    }

    #endregion

    #region IsIdempotentCommand — 幂等判断纯函数

    /// <summary>IIdempotent 命令 → true</summary>
    [Fact]
    public void IsIdempotent_IdempotentCmd_ReturnsTrue() {
        PureFuncActor.IsIdempotentCommand(new PureIdempotentCmd()).Should().BeTrue();
    }

    /// <summary>普通命令(未实现 IIdempotent) → false</summary>
    [Fact]
    public void IsIdempotent_PlainCmd_ReturnsFalse() {
        PureFuncActor.IsIdempotentCommand(new PureCmd()).Should().BeFalse();
    }

    /// <summary>null 命令 → false(空引用不实现任何接口)</summary>
    [Fact]
    public void IsIdempotent_NullCmd_ReturnsFalse() {
        PureFuncActor.IsIdempotentCommand((PureCmd)null!).Should().BeFalse();
    }

    #endregion
}

/// <summary>纯函数测试用 Actor — 仅用于具体化泛型调用 static 方法,不实例化(不启动 Consumer)</summary>
internal sealed class PureFuncActor : ActorBase<PureCmd, Unit> {
    protected override void Handle(PureCmd command, CancellationToken ct) { }
}

/// <summary>普通测试命令(非幂等)</summary>
internal record PureCmd;

/// <summary>幂等测试命令 — 继承 PureCmd 并实现 IIdempotent 标记接口</summary>
internal sealed record PureIdempotentCmd : PureCmd, IIdempotent;
