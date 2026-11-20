namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069 ultrathink 彩虹文字 — 对齐基准 utils/thinking.ts：
/// findThinkingTriggerPositions(/\bultrathink\b/gi) + getRainbowColor(charIndex % 7)。
/// </summary>
public sealed class UltrathinkHighlighterTests
{
    [Theory]
    [InlineData("ultrathink", 0, 10)]
    [InlineData("please ULTRATHINK now", 7, 17)]
    [InlineData("think ultrathink ultrathink", 6, 16)]
    public void FindTriggerPositions_WordBoundaryCaseInsensitive(string text, int start, int end)
    {
        var positions = UltrathinkHighlighter.FindTriggerPositions(text);
        Assert.Contains((start, end), positions);
    }

    [Theory]
    [InlineData("preultrathink", 0)]   // 单词边界：前缀粘连不算
    [InlineData("ultrathinking", 0)]   // 后缀粘连不算
    [InlineData("no keyword here", 0)]
    public void FindTriggerPositions_NoMatch_WhenBoundaryBroken(string text, int expectedCount)
    {
        Assert.Equal(expectedCount, UltrathinkHighlighter.FindTriggerPositions(text).Count);
    }

    [Fact]
    public void FindTriggerPositions_MultipleMatches()
    {
        var positions = UltrathinkHighlighter.FindTriggerPositions("ultrathink and ultrathink");
        Assert.Equal(2, positions.Count);
    }

    [Theory]
    [InlineData(0, 0)]  // 第 0 字符 → rainbow[0]
    [InlineData(6, 6)]  // 第 6 字符 → rainbow[6]
    [InlineData(7, 0)]  // 第 7 字符 → rainbow[0] 循环
    [InlineData(14, 0)] // 第 14 字符 → rainbow[0] 循环
    public void RainbowColorIndex_Modulo7(int charIndex, int expected)
    {
        Assert.Equal(expected, UltrathinkHighlighter.RainbowColorIndex(charIndex));
    }
}
