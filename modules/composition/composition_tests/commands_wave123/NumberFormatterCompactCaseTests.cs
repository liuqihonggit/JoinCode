namespace Composition.Tests.Commands;

/// <summary>
/// TASK069-S5 — token compact 后缀大小写对齐基准（Claude Code format.ts:124-130）：
/// 基准用大写 K/M/B（1.3K），C# 原为小写 k（1.3k）。改 FormatCompact 后缀全大写，
/// 锁定三档（K/M/B）+ 小于 1000 原样输出，防回退。
/// </summary>
public sealed class NumberFormatterCompactCaseTests {
    [Theory]
    [InlineData(1_300, "1.3K")]
    [InlineData(1_000, "1.0K")]
    [InlineData(2_500_000, "2.5M")]
    [InlineData(3_200_000_000L, "3.2B")]
    public void FormatCompact_UsesUpperCaseSuffixes(long value, string expected) =>
        Assert.Equal(expected, JoinCode.Cli.Display.NumberFormatter.FormatCompact(value));

    [Fact]
    public void FormatCompact_BelowThousand_NoSuffix() =>
        Assert.Equal("999", JoinCode.Cli.Display.NumberFormatter.FormatCompact(999));
}
