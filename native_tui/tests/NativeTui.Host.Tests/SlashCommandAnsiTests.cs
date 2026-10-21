namespace NativeTui.Host.Tests;

/// <summary>
/// 命令输出 ANSI 剥离测试（用户验收反馈：`/btw` 输出 "38;2;153;153;153m── 侧边问题 ──" 残骸）—
/// 引擎侧命令彩色输出与 TUI Cell/StyleId 渲染模型不兼容，DispatchSlashCommandAsync 上屏前剥离；
/// 复用引擎 AnsiStripper（abstractions — stackalloc 零分配全序列剥离，勿重复造轮子）。
/// </summary>
public sealed class SlashCommandAnsiTests
{
    /// <summary>SGR 色码序列全剥离（用户案例形态 + 多码混合 + 无码直返）。</summary>
    [Theory]
    [InlineData("\x1b[38;2;153;153;153m── 侧边问题 ──\x1b[0m", "── 侧边问题 ──")]
    [InlineData("普通文本", "普通文本")]
    [InlineData("\x1b[31m红\x1b[0m与\x1b[1m粗\x1b[0m", "红与粗")]
    public void AnsiStripper_SgrSequencesStripped(string input, string expected)
    {
        Assert.Equal(expected, JoinCode.Abstractions.Utils.AnsiStripper.Strip(input));
    }
}
