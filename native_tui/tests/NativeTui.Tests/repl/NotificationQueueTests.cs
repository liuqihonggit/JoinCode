namespace NativeTui.Tests.Repl;

/// <summary>
/// 通知队列状态机测试 — 对齐基准 context/notifications.tsx 语义（帧拍时间模型）：
/// 优先级小值出队（immediate &lt; high &lt; medium &lt; low）+ 同级 FIFO、8s 默认超时/自定义超时、
/// Immediate 抢占（原通知回队）+ 同 key fold 合并（xN 计数 + 计时重置）、
/// Remove 按键清除、空文本忽略。全部时间戳注入（纯函数化 — 禁墙钟）。
/// </summary>
public sealed class NotificationQueueTests
{
    [Fact]
    public void Enqueue_EmptyQueue_ShowsImmediately()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);

        notifications.Enqueue("已切换计划模式");

        Assert.Equal("已切换计划模式", notifications.Current);
        Assert.Equal(0, notifications.QueuedCount);
    }

    [Fact]
    public void Enqueue_EmptyText_Ignored()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);

        notifications.Enqueue(string.Empty);

        Assert.Null(notifications.Current);
        Assert.Equal(0, notifications.QueuedCount);
    }

    [Fact]
    public void Tick_DefaultTimeout8s_AutoClears()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("通知");

        notifications.Tick(7_999);
        Assert.Equal("通知", notifications.Current);

        notifications.Tick(8_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Tick_CustomTimeout_Respected()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("闪现", timeout: TimeSpan.FromMilliseconds(3_000));

        notifications.Tick(2_999);
        Assert.Equal("闪现", notifications.Current);

        notifications.Tick(3_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Tick_Expiry_DequeuesNext()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("第一条");
        notifications.Enqueue("第二条");

        notifications.Tick(8_000);

        // 第一条到期 → 第二条自动接上（计时起点 = 出道帧）
        Assert.Equal("第二条", notifications.Current);
        notifications.Tick(15_999);
        Assert.Equal("第二条", notifications.Current);
        notifications.Tick(16_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Priority_SmallValueDequeuesFirst()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("low", NotificationPriority.Low);
        notifications.Enqueue("high", NotificationPriority.High);
        notifications.Enqueue("medium", NotificationPriority.Medium);
        Assert.Equal("low", notifications.Current);

        notifications.Tick(8_000);
        // low 到期 → high（1）先于 medium（2）
        Assert.Equal("high", notifications.Current);

        notifications.Tick(16_000);
        Assert.Equal("medium", notifications.Current);
    }

    [Fact]
    public void Priority_SameLevel_Fifo()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("先");
        notifications.Enqueue("后");

        notifications.Tick(8_000);

        Assert.Equal("后", notifications.Current);
    }

    [Fact]
    public void Immediate_PreemptsCurrent_OldGoesBackToQueue()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("普通通知");

        notifications.Tick(1_000);
        notifications.Enqueue("紧急通知", NotificationPriority.Immediate);

        Assert.Equal("紧急通知", notifications.Current);
        Assert.Equal(1, notifications.QueuedCount);

        // 紧急通知 1s 起计时 8s → 9_000 到期 → 普通通知接上
        notifications.Tick(9_000);
        Assert.Equal("普通通知", notifications.Current);
    }

    [Fact]
    public void Immediate_CustomTimeout_Respected()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);

        notifications.Enqueue("紧急", NotificationPriority.Immediate, TimeSpan.FromMilliseconds(2_000));

        notifications.Tick(1_999);
        Assert.Equal("紧急", notifications.Current);
        notifications.Tick(2_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Fold_SameKeyCurrent_ShowsXNAndResetsTimeout()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("已批准");

        notifications.Tick(1_000);
        notifications.Enqueue("已批准");
        Assert.Equal("已批准 x2", notifications.Current);

        notifications.Tick(8_999);
        // 1s 起重新计时 → 8_999（7999ms）未到期（旧起点 0 的话 8999 已超时 — 重置验证）
        Assert.Equal("已批准 x2", notifications.Current);

        notifications.Tick(9_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Fold_SameKeyQueued_MergesCount()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("当前");
        notifications.Enqueue("排队", NotificationPriority.Low);

        notifications.Tick(1_000);
        notifications.Enqueue("排队", NotificationPriority.Low);
        Assert.Equal(1, notifications.QueuedCount);

        notifications.Tick(8_000);
        // 当前到期 → 出队合并后的排队项（x2）
        Assert.Equal("排队 x2", notifications.Current);
    }

    [Fact]
    public void Fold_ImmediateSameKey_AbsorbsCurrent()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("警报");

        notifications.Tick(1_000);
        notifications.Enqueue("警报", NotificationPriority.Immediate);

        Assert.Equal("警报 x2", notifications.Current);
        Assert.Equal(0, notifications.QueuedCount);
    }

    [Fact]
    public void Remove_CurrentKey_ClearsAndDequeuesNext()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("第一条");
        notifications.Enqueue("第二条");

        notifications.Remove("第一条");

        Assert.Equal("第二条", notifications.Current);
    }

    [Fact]
    public void Remove_UnknownKey_NoOp()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("保留");

        notifications.Remove("不存在的键");

        Assert.Equal("保留", notifications.Current);
        Assert.Equal(0, notifications.QueuedCount);
    }

    [Fact]
    public void Remove_QueuedKey_RemovesFromQueue()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("当前");
        notifications.Enqueue("待移除", NotificationPriority.Low);

        notifications.Remove("待移除");

        Assert.Equal(0, notifications.QueuedCount);
        notifications.Tick(8_000);
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Clear_EmptiesCurrentAndQueue()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("当前");
        notifications.Enqueue("排队");

        notifications.Clear();

        Assert.Null(notifications.Current);
        Assert.Equal(0, notifications.QueuedCount);
    }

    [Fact]
    public void CurrentKey_ExposesIdentity()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("通知文本");
        notifications.Enqueue("通知文本");

        Assert.Equal("通知文本", notifications.CurrentKey);
        Assert.Equal("通知文本 x2", notifications.Current);
    }
}
