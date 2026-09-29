namespace Core.Utils;

/// <summary>
/// AskAwait 纯函数确定性单元测试 — 验证超时判断、死锁异常创建,不依赖时序/不启动 Actor。
/// <para>覆盖:超时 vs 外部取消区分(未取消/已取消/default/None)、死锁异常属性(actorName/timeoutMs/Message)。</para>
/// <para>static 方法通过 PureFuncActor(AskRetryPureFunctionsTest 中定义)调用,不实例化 Actor,无时序依赖。</para>
/// </summary>
public class AskAwaitPureFunctionsTest {

    #region IsTimeoutCancellation — 超时 vs 外部取消判断纯函数

    /// <summary>未取消的令牌 → true(取消由超时触发,非外部)</summary>
    [Fact]
    public void IsTimeout_NotCancelledToken_ReturnsTrue() {
        using var cts = new CancellationTokenSource();
        PureFuncActor.IsTimeoutCancellation(cts.Token).Should().BeTrue("令牌未取消,取消由超时触发");
    }

    /// <summary>已取消的令牌 → false(取消由外部触发,非超时)</summary>
    [Fact]
    public void IsTimeout_CancelledToken_ReturnsFalse() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        PureFuncActor.IsTimeoutCancellation(cts.Token).Should().BeFalse("令牌已取消,取消由外部触发");
    }

    /// <summary>default(CancellationToken) → true(未取消)</summary>
    [Fact]
    public void IsTimeout_DefaultToken_ReturnsTrue() {
        PureFuncActor.IsTimeoutCancellation(default).Should().BeTrue("default 令牌永不取消");
    }

    /// <summary>CancellationToken.None → true(未取消)</summary>
    [Fact]
    public void IsTimeout_NoneToken_ReturnsTrue() {
        PureFuncActor.IsTimeoutCancellation(CancellationToken.None).Should().BeTrue("None 令牌永不取消");
    }

    /// <summary>取消后判断 → false(时序无关,只看当前状态)</summary>
    [Fact]
    public void IsTimeout_AfterCancel_ReturnsFalse() {
        using var cts = new CancellationTokenSource();
        PureFuncActor.IsTimeoutCancellation(cts.Token).Should().BeTrue("取消前:超时触发");
        cts.Cancel();
        PureFuncActor.IsTimeoutCancellation(cts.Token).Should().BeFalse("取消后:外部触发");
    }

    #endregion

    #region CreateAskDeadlockException — 死锁异常工厂纯函数

    /// <summary>正常参数 → ActorName/TimeoutMs 属性正确</summary>
    [Fact]
    public void CreateDeadlock_NormalArgs_PropertiesCorrect() {
        var ex = PureFuncActor.CreateAskDeadlockException("MyActor", 5000);
        ex.ActorName.Should().Be("MyActor");
        ex.TimeoutMs.Should().Be(5000);
    }

    /// <summary>异常应为 TimeoutException 子类(调用方可用 catch(TimeoutException) 捕获)</summary>
    [Fact]
    public void CreateDeadlock_IsTimeoutException() {
        var ex = PureFuncActor.CreateAskDeadlockException("A", 100);
        ex.Should().BeAssignableTo<TimeoutException>();
    }

    /// <summary>Message 包含 actorName 和 timeoutMs(诊断信息)</summary>
    [Fact]
    public void CreateDeadlock_MessageContainsActorNameAndTimeout() {
        var ex = PureFuncActor.CreateAskDeadlockException("DiagnosticActor", 3000);
        ex.Message.Should().Contain("DiagnosticActor");
        ex.Message.Should().Contain("3000");
    }

    /// <summary>空 actorName → 属性为空(边界,不抛异常)</summary>
    [Fact]
    public void CreateDeadlock_EmptyActorName_NoThrow() {
        var ex = PureFuncActor.CreateAskDeadlockException("", 1000);
        ex.ActorName.Should().BeEmpty();
        ex.TimeoutMs.Should().Be(1000);
    }

    /// <summary>timeoutMs=0 → 属性为0(边界)</summary>
    [Fact]
    public void CreateDeadlock_ZeroTimeout_NoThrow() {
        var ex = PureFuncActor.CreateAskDeadlockException("ZeroActor", 0);
        ex.TimeoutMs.Should().Be(0);
    }

    /// <summary>相同参数创建的异常属性相等(确定性)</summary>
    [Fact]
    public void CreateDeadlock_SameArgs_PropertiesEqual() {
        var ex1 = PureFuncActor.CreateAskDeadlockException("Same", 2000);
        var ex2 = PureFuncActor.CreateAskDeadlockException("Same", 2000);
        ex1.ActorName.Should().Be(ex2.ActorName);
        ex1.TimeoutMs.Should().Be(ex2.TimeoutMs);
    }

    #endregion
}
