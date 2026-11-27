namespace NativeTui.Tests.Repl.Input;

/// <summary>
/// 代理对累加器测试 — Win32 KEY_EVENT 逐 UTF-16 码元到达，emoji 必须凑成完整代理对才进输入缓冲。
/// 真机取证：粘贴 👍 后 transcript 落盘两个 U+FFFD（孤立代理被逐半插入）。
/// </summary>
public sealed class SurrogatePairAccumulatorTests
{
    private const string ThumbsUp = "\U0001F44D";

    [Fact]
    public void Feed_HighThenLow_ReturnsCompletePair()
    {
        var accumulator = new SurrogatePairAccumulator();

        Assert.Null(accumulator.Feed(ThumbsUp[0]));
        Assert.Equal(ThumbsUp, accumulator.Feed(ThumbsUp[1]));
    }

    [Fact]
    public void Feed_IsolatedHigh_ReturnsNull()
    {
        var accumulator = new SurrogatePairAccumulator();

        Assert.Null(accumulator.Feed(ThumbsUp[0]));
        Assert.Null(accumulator.Feed(ThumbsUp[0]));
    }

    [Fact]
    public void Feed_HighThenAscii_DropsHighAndClearsPending()
    {
        var accumulator = new SurrogatePairAccumulator();
        Assert.Null(accumulator.Feed(ThumbsUp[0]));

        Assert.Null(accumulator.Feed('a'));
        // 普通键后 pending 清空 — 后续孤低代理不得与旧高代理错配
        Assert.Null(accumulator.Feed(ThumbsUp[1]));
    }

    [Fact]
    public void Feed_IsolatedLow_ReturnsNull()
    {
        var accumulator = new SurrogatePairAccumulator();

        Assert.Null(accumulator.Feed(ThumbsUp[1]));
    }

    [Fact]
    public void Reset_DiscardsPendingHigh()
    {
        var accumulator = new SurrogatePairAccumulator();
        accumulator.Feed(ThumbsUp[0]);

        accumulator.Reset();

        Assert.Null(accumulator.Feed(ThumbsUp[1]));
    }

    [Fact]
    public void InputBuffer_InsertText_KeepsSurrogatePairIntact()
    {
        var buffer = new InputBuffer();
        buffer.InsertText(ThumbsUp);

        Assert.Equal(ThumbsUp, buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
        Assert.False(buffer.Text.Contains('\uFFFD', StringComparison.Ordinal));
    }
}
