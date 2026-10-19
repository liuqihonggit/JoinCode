namespace NativeTui.Tests.Core.Formatting;

/// <summary>
/// 纯格式化工具测试 — 对齐基准 utils/format.ts：
/// formatDuration（&lt;60s 秒 / 60 进位链 / d-h-m 分层）与 formatNumber（compact 1 位小写）。
/// 期望值由基准实现逐分支推演。
/// </summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData(0L, "0s")]
    [InlineData(500L, "0s")]
    [InlineData(1000L, "1s")]
    [InlineData(59999L, "59s")]
    public void Duration_BelowMinute_Seconds(long ms, string expected) =>
        Assert.Equal(expected, Format.Duration(ms));

    [Theory]
    [InlineData(60000L, "1m 0s")]
    [InlineData(60500L, "1m 1s")]
    [InlineData(90000L, "1m 30s")]
    public void Duration_Minutes(long ms, string expected) =>
        Assert.Equal(expected, Format.Duration(ms));

    [Theory]
    [InlineData(3599500L, "1h 0m 0s")]
    [InlineData(3661000L, "1h 1m 1s")]
    [InlineData(7203000L, "2h 0m 3s")]
    public void Duration_Hours_WithCarryChain(long ms, string expected) =>
        Assert.Equal(expected, Format.Duration(ms));

    [Theory]
    [InlineData(86_400_000L, "1d 0h 0m")]
    [InlineData(90_061_000L, "1d 1h 1m")]
    public void Duration_Days_NoSeconds(long ms, string expected) =>
        Assert.Equal(expected, Format.Duration(ms));

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1000L, "1.0k")]
    [InlineData(1321L, "1.3k")]
    [InlineData(9999L, "10.0k")]
    [InlineData(123_456L, "123.5k")]
    [InlineData(1_500_000L, "1.5m")]
    [InlineData(2_500_000_000L, "2.5b")]
    public void Number_CompactNotation(long value, string expected) =>
        Assert.Equal(expected, Format.Number(value));
}
