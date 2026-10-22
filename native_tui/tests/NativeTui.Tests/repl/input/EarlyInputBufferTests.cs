namespace NativeTui.Tests.Repl.Input;

/// <summary>
/// 启动期输入捕获测试 — 基准 utils/earlyInput.ts:29-67,164-169（REPL 初始化完成前的按键
/// 缓冲并回填输入框）：引擎组装耗时窗口内的按键不丢失；无按键零等待立即返回。
/// </summary>
public sealed class EarlyInputBufferTests
{
    /// <summary>可用按键全部捕获（不回显 — intercept: true 逐键读取）。</summary>
    [Fact]
    public void Capture_AvailableKeys_ReadsAllWithIntercept()
    {
        var pending = new Queue<ConsoleKeyInfo>(
        [
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);
        var interceptFlags = new List<bool>();

        var captured = EarlyInputBuffer.CaptureAvailable(
            keyAvailable: () => pending.Count > 0,
            readKey: intercept =>
            {
                interceptFlags.Add(intercept);
                return pending.Dequeue();
            });

        Assert.Equal(2, captured.Count);
        Assert.Equal(ConsoleKey.A, captured[0].Key);
        Assert.Equal(ConsoleKey.Enter, captured[1].Key);
        Assert.All(interceptFlags, static flag => Assert.True(flag));
    }

    /// <summary>无按键 — 返回空列表零等待（启动路径不阻塞）。</summary>
    [Fact]
    public void Capture_NoKeys_ReturnsEmpty()
    {
        var captured = EarlyInputBuffer.CaptureAvailable(
            keyAvailable: () => false,
            readKey: _ => throw new InvalidOperationException("无按键不得读取"));

        Assert.Empty(captured);
    }
}
