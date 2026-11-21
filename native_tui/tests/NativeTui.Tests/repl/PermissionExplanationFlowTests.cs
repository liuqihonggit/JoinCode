namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069-P4 — ctrl+e 模型解释的 TUI 侧闭环：懒加载（首次展开才发请求）、三态（Loading/Ready/Unavailable）、
/// 收起不丢缓存、失败不重试、开关关闭整体 no-op、对话框结束时取消在途请求。
/// <para>为什么这些必须在 shell 层测而不是只测协调器：请求是线程池上跑的，结果要靠主循环排空队列才落定 ——
/// 少接一次排空，界面就会永远停在「正在生成解释」。</para>
/// </summary>
public sealed class PermissionExplanationFlowTests
{
    private static readonly PermissionExplanationView Sample =
        new("列出当前目录的文件", "我需要确认仓库根目录的内容", "只读操作，不会改动文件", PermissionRiskBand.Low);

    /// <summary>构造挂起权限对话框的测试 shell。</summary>
    private static ReplShell CreateShellWithPendingPermission()
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        engine.Events.Enqueue(new BridgeEvent.PermissionRequest("Bash", "运行演示命令"));
        shell.DrainEngine();
        return shell;
    }

    /// <summary>轮询排空直到谓词成立（上限 2 秒 — 后台任务完成即入队，主循环排空后落定）。</summary>
    private static void PumpUntil(ReplShell shell, Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline)
        {
            shell.DrainEngine();
            if (condition())
            {
                return;
            }

            Thread.Sleep(5);
        }
    }

    // ---- 懒加载与三态 ----

    /// <summary>未按 ctrl+e 就不发请求（对齐基准「避免为永不查看的解释消耗 token」）。</summary>
    [Fact]
    public void WithoutCtrlE_NoRequestIsMade()
    {
        var shell = CreateShellWithPendingPermission();
        var calls = 0;
        shell.BindPermissionExplainer(true, (_, _, _) => {
            calls++;
            return Task.FromResult<PermissionExplanationView?>(Sample);
        });

        Assert.Equal(PermissionExplanationPhase.Hidden, shell.Permission.ExplanationPhase);
        Assert.Equal(0, calls);
    }

    /// <summary>按 ctrl+e → 立即进加载态（有加载器时不能空转），请求完成后落就绪并缓存结果。</summary>
    [Fact]
    public void CtrlE_LoadsThenBecomesReady()
    {
        var shell = CreateShellWithPendingPermission();
        var gate = new TaskCompletionSource<PermissionExplanationView?>();
        shell.BindPermissionExplainer(true, (_, _, _) => gate.Task);

        shell.ToggleExplanationRequest();

        Assert.Equal(PermissionExplanationPhase.Loading, shell.Permission.ExplanationPhase);
        Assert.True(shell.Permission.ExplanationVisible);

        gate.SetResult(Sample);
        PumpUntil(shell, () => shell.Permission.ExplanationPhase == PermissionExplanationPhase.Ready);

        Assert.Equal(PermissionExplanationPhase.Ready, shell.Permission.ExplanationPhase);
        Assert.Equal(Sample, shell.Permission.Explanation);
    }

    /// <summary>加载器返回 null → 不可用（不抛、不留 Loading）。</summary>
    [Fact]
    public void CtrlE_LoaderReturnsNull_BecomesUnavailable()
    {
        var shell = CreateShellWithPendingPermission();
        shell.BindPermissionExplainer(true, (_, _, _) => Task.FromResult<PermissionExplanationView?>(null));

        shell.ToggleExplanationRequest();
        PumpUntil(shell, () => shell.Permission.ExplanationPhase != PermissionExplanationPhase.Loading);

        Assert.Equal(PermissionExplanationPhase.Unavailable, shell.Permission.ExplanationPhase);
    }

    /// <summary>宿主没有解释能力（未接模型）→ 直接不可用，绝不假装转圈。</summary>
    [Fact]
    public void CtrlE_NoLoader_BecomesUnavailable()
    {
        var shell = CreateShellWithPendingPermission();
        shell.BindPermissionExplainer(true, loader: null);

        shell.ToggleExplanationRequest();

        Assert.Equal(PermissionExplanationPhase.Unavailable, shell.Permission.ExplanationPhase);
    }

    // ---- 收起 / 缓存 / 不重试 ----

    /// <summary>收起不丢缓存：再按 ctrl+e 直接显示，且不重新发请求（token 只花一次）。</summary>
    [Fact]
    public void HideThenShowAgain_ReusesCacheWithoutNewRequest()
    {
        var shell = CreateShellWithPendingPermission();
        var calls = 0;
        shell.BindPermissionExplainer(true, (_, _, _) => {
            calls++;
            return Task.FromResult<PermissionExplanationView?>(Sample);
        });

        shell.ToggleExplanationRequest();
        PumpUntil(shell, () => shell.Permission.ExplanationPhase == PermissionExplanationPhase.Ready);
        shell.ToggleExplanationRequest();   // 收起
        Assert.Equal(PermissionExplanationPhase.Hidden, shell.Permission.ExplanationPhase);

        shell.ToggleExplanationRequest();   // 再展开（走缓存）

        Assert.Equal(PermissionExplanationPhase.Ready, shell.Permission.ExplanationPhase);
        Assert.Equal(1, calls);
    }

    /// <summary>失败后重按不再重试（一次对话框一次请求 — 对齐基准把 promise 缓存下来的行为）。</summary>
    [Fact]
    public void AfterUnavailable_PressAgainDoesNotRetry()
    {
        var shell = CreateShellWithPendingPermission();
        var calls = 0;
        shell.BindPermissionExplainer(true, (_, _, _) => {
            calls++;
            return Task.FromResult<PermissionExplanationView?>(null);
        });

        shell.ToggleExplanationRequest();
        PumpUntil(shell, () => shell.Permission.ExplanationPhase == PermissionExplanationPhase.Unavailable);
        shell.ToggleExplanationRequest();   // 收起
        shell.ToggleExplanationRequest();   // 再展开

        Assert.Equal(PermissionExplanationPhase.Unavailable, shell.Permission.ExplanationPhase);
        Assert.Equal(1, calls);
    }

    // ---- 开关 ----

    /// <summary>开关关闭（enabled=false）→ ctrl+e 整体 no-op：不发请求、不加行。</summary>
    [Fact]
    public void Disabled_CtrlEIsNoOp()
    {
        var shell = CreateShellWithPendingPermission();
        var calls = 0;
        shell.BindPermissionExplainer(false, (_, _, _) => {
            calls++;
            return Task.FromResult<PermissionExplanationView?>(Sample);
        });

        shell.ToggleExplanationRequest();

        Assert.Equal(PermissionExplanationPhase.Hidden, shell.Permission.ExplanationPhase);
        Assert.Equal(0, calls);
    }

    // ---- 生命周期 ----

    /// <summary>加载中收起 → 取消在途请求（不让它跑完整超时）并回到隐藏态。</summary>
    [Fact]
    public async Task HideWhileLoading_CancelsRequest()
    {
        var shell = CreateShellWithPendingPermission();
        var gate = new TaskCompletionSource<PermissionExplanationView?>();
        var started = new TaskCompletionSource<CancellationToken>();
        shell.BindPermissionExplainer(true, (_, _, ct) => {
            started.TrySetResult(ct);
            return gate.Task;
        });

        shell.ToggleExplanationRequest();
        var seen = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        shell.ToggleExplanationRequest();

        Assert.Equal(PermissionExplanationPhase.Hidden, shell.Permission.ExplanationPhase);
        Assert.True(seen.IsCancellationRequested);
    }

    /// <summary>对话框提交 → 在途请求随之中止（用户已经做了决定，解释没有存在意义）。</summary>
    [Fact]
    public async Task CompletingPermission_CancelsRequest()
    {
        var shell = CreateShellWithPendingPermission();
        var gate = new TaskCompletionSource<PermissionExplanationView?>();
        var started = new TaskCompletionSource<CancellationToken>();
        shell.BindPermissionExplainer(true, (_, _, ct) => {
            started.TrySetResult(ct);
            return gate.Task;
        });
        shell.ToggleExplanationRequest();
        var seen = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        shell.HandleKey(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false));

        Assert.True(seen.IsCancellationRequested);
        Assert.False(shell.Permission.IsPending);
    }
}
