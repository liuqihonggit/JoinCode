namespace NativeTui.Tests.Repl;

/// <summary>
/// S8 输入请求协调器测试 — InputRequestCoordinator（prompt/readPassword 命令交互回调的 TCS 桥，
/// PermissionCoordinator 模式复刻：Begin 取代/Complete 幂等/Wait 无挂起占位 null）
/// + PasswordMask（readPassword 等宽 * 遮蔽 — 数据层，渲染接线 S7 域未决）。
/// </summary>
public sealed class PermissionInputRequestTests
{
    /// <summary>构造挂起输入请求的 coordinator（默认文本输入）。</summary>
    private static InputRequestCoordinator BeginCoordinator(InputRequestKind kind = InputRequestKind.Text)
    {
        var coordinator = new InputRequestCoordinator();
        coordinator.Begin(new InputRequest(kind, "请输入内容"));
        return coordinator;
    }

    /// <summary>Begin 挂起请求 — IsPending + Request 就位（种类/提示透传）。</summary>
    [Fact]
    public void Begin_SetsPendingRequest()
    {
        var coordinator = BeginCoordinator(InputRequestKind.Password);

        Assert.True(coordinator.IsPending);
        Assert.Equal(InputRequestKind.Password, coordinator.Request?.Kind);
        Assert.Equal("请输入内容", coordinator.Request?.Prompt);
    }

    /// <summary>Begin 二次请求 — 旧等待者取消（对齐 PermissionCoordinator 取代语义）。</summary>
    [Fact]
    public async Task Begin_SecondRequest_CancelsFirstWaiter()
    {
        var coordinator = BeginCoordinator();
        var first = coordinator.Wait();

        coordinator.Begin(new InputRequest(InputRequestKind.Password, "请输入密码"));

        await Assert.ThrowsAsync<TaskCanceledException>(() => first);
        Assert.Equal(InputRequestKind.Password, coordinator.Request?.Kind);
    }

    /// <summary>Complete — 等待者拿到输入 + 状态清空（null 输入 = 用户取消）。</summary>
    [Fact]
    public async Task Complete_ResolvesWaiterWithInput()
    {
        var coordinator = BeginCoordinator();
        var task = coordinator.Wait();

        coordinator.Complete("hello world");

        Assert.Equal("hello world", await task);
        Assert.False(coordinator.IsPending);
        Assert.Null(coordinator.Request);
    }

    /// <summary>Complete null — 用户取消语义（等待者拿到 null）。</summary>
    [Fact]
    public async Task Complete_Null_ResolvesAsCancelled()
    {
        var coordinator = BeginCoordinator();
        var task = coordinator.Wait();

        coordinator.Complete(null);

        Assert.Null(await task);
        Assert.False(coordinator.IsPending);
    }

    /// <summary>无挂起时 Complete — no-op 不抛。</summary>
    [Fact]
    public void Complete_WithoutBegin_NoOp()
    {
        var coordinator = new InputRequestCoordinator();

        coordinator.Complete("x");

        Assert.False(coordinator.IsPending);
    }

    /// <summary>Complete 幂等 — 二次 Complete 不影响首个结果。</summary>
    [Fact]
    public async Task Complete_Twice_KeepsFirstResult()
    {
        var coordinator = BeginCoordinator();
        var task = coordinator.Wait();

        coordinator.Complete("first");
        coordinator.Complete("second");

        Assert.Equal("first", await task);
    }

    /// <summary>无挂起时 Wait — 已完成 null 占位任务（Host 回调安全值）。</summary>
    [Fact]
    public async Task Wait_WithoutBegin_CompletesWithNull()
    {
        var coordinator = new InputRequestCoordinator();

        Assert.Null(await coordinator.Wait());
    }

    // ---- PasswordMask ----

    /// <summary>遮蔽 — 每字符替换 '*' 长度 1:1 保持。</summary>
    [Theory]
    [InlineData("abc123", "******")]
    [InlineData("a", "*")]
    [InlineData("", "")]
    public void PasswordMask_MasksEachChar(string input, string expected)
    {
        Assert.Equal(expected, PasswordMask.Of(input));
    }

    /// <summary>遮蔽长度保持 — 光标/退格语义与普通输入一致（渲染层无需宽度特判）。</summary>
    [Fact]
    public void PasswordMask_PreservesLength()
    {
        var input = "p@ssw0rd!中文";

        Assert.Equal(input.Length, PasswordMask.Of(input).Length);
    }
}
