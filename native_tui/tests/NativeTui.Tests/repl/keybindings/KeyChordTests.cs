namespace NativeTui.Tests.Repl;

/// <summary>
/// 键序列测试 — 对齐基准 keybindings/parser.ts parseChord + resolver.ts 序列语义：
/// 空格分隔先后（"ctrl+x ctrl+e" 二段式）、单空格=space 键 quirk、
/// 单键 Matches 不吞序列首键（Phase 1 跳过多键 chord）、IsPrefix 挂起判定、MatchesSequence 完整触发判定。
/// </summary>
public sealed class KeyChordTests
{
    /// <summary>构造按键辅助（测试可读性）。</summary>
    private static KeyPress Ctrl(char letter) => new(KeyKind.Char, letter.ToString(), Ctrl: true);

    /// <summary>二段式序列解析：IsSequence + 两步。</summary>
    [Fact]
    public void Parse_TwoStepSequence_ProducesTwoSteps()
    {
        var chord = KeyChord.Parse("ctrl+x ctrl+e");
        Assert.True(chord.IsSequence);
        Assert.Equal(2, chord.Steps.Length);
        Assert.Equal("ctrl+x ctrl+e", chord.ToString());
    }

    /// <summary>单键序列：IsSequence=false 且 Matches 单键。</summary>
    [Fact]
    public void Parse_SingleKey_IsNotSequence()
    {
        var chord = KeyChord.Parse("ctrl+g");
        Assert.False(chord.IsSequence);
        Assert.True(chord.Matches(Ctrl('g')));
        Assert.Equal("ctrl+g", chord.ToString());
    }

    /// <summary>孤立空格字符串 = space 键（基准 parser.ts:81-83 quirk）。</summary>
    [Fact]
    public void Parse_LoneSpaceMeansSpaceKey()
    {
        var chord = KeyChord.Parse(" ");
        Assert.False(chord.IsSequence);
        Assert.True(chord.Matches(new KeyPress(KeyKind.Char, " ")));
        Assert.Equal("space", chord.ToString());
    }

    /// <summary>序列键 Matches 单键恒 false（不拦截首键事件 — 对齐基准 resolver Phase 1）。</summary>
    [Fact]
    public void Matches_SequenceChordNeverMatchesSingleKey()
    {
        var chord = KeyChord.Parse("ctrl+x ctrl+e");
        Assert.False(chord.Matches(Ctrl('x')));
        Assert.False(chord.Matches(Ctrl('e')));
    }

    /// <summary>IsPrefix 首键挂起判定：ctrl+x 是序列首键、ctrl+e 不是。</summary>
    [Fact]
    public void IsPrefix_DetectsSequenceFirstStep()
    {
        var chord = KeyChord.Parse("ctrl+x ctrl+e");
        Assert.True(chord.IsPrefix(Ctrl('x')));
        Assert.False(chord.IsPrefix(Ctrl('e')));
        Assert.False(chord.IsPrefix(new KeyPress(KeyKind.Char, "x")));
    }

    /// <summary>IsPrefixOf 严格前缀：[ctrl+x] 是前缀；等长序列与空序列不是。</summary>
    [Fact]
    public void IsPrefixOf_StrictPrefixOnly()
    {
        var chord = KeyChord.Parse("ctrl+x ctrl+e");
        Assert.True(chord.IsPrefixOf([Ctrl('x')]));
        Assert.False(chord.IsPrefixOf([Ctrl('x'), Ctrl('e')]));
        Assert.False(chord.IsPrefixOf([Ctrl('e')]));
        Assert.False(chord.IsPrefixOf([]));
    }

    /// <summary>MatchesSequence 完整触发：顺序与修饰全等才命中。</summary>
    [Fact]
    public void MatchesSequence_RequiresExactStepsInOrder()
    {
        var chord = KeyChord.Parse("ctrl+x ctrl+e");
        Assert.True(chord.MatchesSequence([Ctrl('x'), Ctrl('e')]));
        Assert.False(chord.MatchesSequence([Ctrl('e'), Ctrl('x')]));
        Assert.False(chord.MatchesSequence([Ctrl('x')]));
        Assert.False(chord.MatchesSequence([Ctrl('x'), Ctrl('e'), Ctrl('g')]));
    }

    /// <summary>解析失败给段定位（第几段 + 该段原文）。</summary>
    [Theory]
    [InlineData("ctrl+x F13", "第 2 段")]
    [InlineData("cmd+p ctrl+e", "第 1 段")]
    [InlineData("ctrl+x ctrl+", "第 2 段")]
    public void TryParse_FailureReportsStepLocation(string text, string expectedLocation)
    {
        var ok = KeyChord.TryParse(text, out _, out var error);
        Assert.False(ok);
        Assert.Contains(expectedLocation, error);
    }

    /// <summary>空串/全空白拒绝并引导正确写法。</summary>
    [Fact]
    public void TryParse_EmptyInputRejected()
    {
        Assert.False(KeyChord.TryParse("", out _, out var error));
        Assert.Contains("KeyChord 解析层拦截", error);
        Assert.Contains("正确写法", error);
    }
}
