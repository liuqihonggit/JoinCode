namespace NativeTui.Tests.Repl;

/// <summary>
/// 引擎死亡检测测试（TASK069 B-2 — 对齐基准 finally 无条件复位语义）：
/// 引擎在回合进行中死亡（无终态事件 → IsBusy 卡 true）时，死亡检测仍须触发 —
/// 退出提示上屏 + 主循环结束；旧双条件 `!IsAlive && !IsBusy` 把 busy 中死亡挡在门外
/// （spinner 永转无提示挂死）。
/// </summary>
public sealed class EngineDeathDetectionTests
{
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", @"C:\tmp"), new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>空闲死亡（既有行为回归守卫）— 提示 + 退出。</summary>
    [Fact]
    public void EngineDead_WhileIdle_ExitsWithMessage()
    {
        var (shell, engine) = CreateShell();
        engine.Alive = false;

        shell.DrainEngine();

        Assert.False(shell.Running);
    }

    /// <summary>busy 中死亡（B-2）— 死亡检测不再被 !IsBusy 挡住：提示 + 退出（spinner 不永转）。</summary>
    [Fact]
    public void EngineDead_WhileBusy_ExitsWithMessage()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        Assert.True(shell.Running);

        engine.Alive = false;
        // 第一拍 — 缓冲（收尾事件优先：残余终态事件可能下一拍到达）
        shell.DrainEngine();
        Assert.True(shell.Running);
        // 第二拍 — 仍 busy 且无终态（崩溃场景）→ 强制收尾退出（spinner 不永转）
        shell.DrainEngine();

        Assert.False(shell.Running);
    }
}
