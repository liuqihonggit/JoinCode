namespace JoinCode.Entry.Tests;


/// <summary>
/// DebugDumpPromptStep 单元测试 — 验证 ParseDebugDumpInput 各种输入格式解析为 DebugDumpSection 位标志
/// 覆盖: 数字、字母、单词、字母组合、分隔符组合、边界情况
/// </summary>
public class DebugDumpPromptStepTests
{
    #region 数字输入

    [Theory]
    [InlineData("0", DebugDumpSection.None)]
    [InlineData("1", DebugDumpSection.Init)]
    [InlineData("2", DebugDumpSection.Error)]
    [InlineData("4", DebugDumpSection.Warn)]
    [InlineData("8", DebugDumpSection.Log)]
    [InlineData("16", DebugDumpSection.Prompt)]
    [InlineData("31", DebugDumpSection.All)]
    public void ParseDebugDumpInput_Digit_ReturnsCorrespondingFlag(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("3", DebugDumpSection.Init | DebugDumpSection.Error)]
    [InlineData("5", DebugDumpSection.Init | DebugDumpSection.Warn)]
    [InlineData("17", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("24", DebugDumpSection.Log | DebugDumpSection.Prompt)]
    public void ParseDebugDumpInput_DigitCombination_ReturnsBitwiseOr(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ParseDebugDumpInput_DigitExceedingAll_MasksToValidBits()
    {
        // 32 超出 All(31)，应被掩码为 0（无有效位）
        var result = DebugDumpPromptStep.ParseDebugDumpInput("32");
        result.Should().Be(DebugDumpSection.None);
    }

    #endregion

    #region 单字母输入

    [Theory]
    [InlineData("i", DebugDumpSection.Init)]
    [InlineData("e", DebugDumpSection.Error)]
    [InlineData("w", DebugDumpSection.Warn)]
    [InlineData("l", DebugDumpSection.Log)]
    [InlineData("p", DebugDumpSection.Prompt)]
    [InlineData("a", DebugDumpSection.All)]
    public void ParseDebugDumpInput_SingleLetter_ReturnsCorrespondingFlag(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("I", DebugDumpSection.Init)]
    [InlineData("E", DebugDumpSection.Error)]
    [InlineData("A", DebugDumpSection.All)]
    public void ParseDebugDumpInput_UpperCaseLetter_ReturnsCorrespondingFlag(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    #endregion

    #region 单词输入

    [Theory]
    [InlineData("init", DebugDumpSection.Init)]
    [InlineData("error", DebugDumpSection.Error)]
    [InlineData("warn", DebugDumpSection.Warn)]
    [InlineData("log", DebugDumpSection.Log)]
    [InlineData("prompt", DebugDumpSection.Prompt)]
    [InlineData("all", DebugDumpSection.All)]
    [InlineData("none", DebugDumpSection.None)]
    public void ParseDebugDumpInput_Word_ReturnsCorrespondingFlag(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    #endregion

    #region 字母组合（连续字母逐字符匹配）

    [Theory]
    [InlineData("ip", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("ie", DebugDumpSection.Init | DebugDumpSection.Error)]
    [InlineData("wel", DebugDumpSection.Warn | DebugDumpSection.Error | DebugDumpSection.Log)]
    [InlineData("ipl", DebugDumpSection.Init | DebugDumpSection.Prompt | DebugDumpSection.Log)]
    public void ParseDebugDumpInput_ContinuousLetters_ReturnsBitwiseOr(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    #endregion

    #region 分隔符组合

    [Theory]
    [InlineData("i,p", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("i+p", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("i p", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("i|p", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("init prompt", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("init,prompt", DebugDumpSection.Init | DebugDumpSection.Prompt)]
    [InlineData("i e w l p", DebugDumpSection.All)]
    public void ParseDebugDumpInput_SeparatorCombination_ReturnsBitwiseOr(string input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    #endregion

    #region 边界情况

    [Theory]
    [InlineData("", DebugDumpSection.None)]
    [InlineData(" ", DebugDumpSection.None)]
    [InlineData(null, DebugDumpSection.None)]
    [InlineData("xyz", DebugDumpSection.None)]
    [InlineData("999", DebugDumpSection.None)]
    public void ParseDebugDumpInput_InvalidInput_ReturnsNone(string? input, DebugDumpSection expected)
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ParseDebugDumpInput_WhitespaceAroundInput_IsTrimmed()
    {
        var result = DebugDumpPromptStep.ParseDebugDumpInput("  a  ");
        result.Should().Be(DebugDumpSection.All);
    }

    #endregion
}
