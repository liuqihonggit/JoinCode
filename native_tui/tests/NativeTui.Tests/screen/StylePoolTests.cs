namespace NativeTui.Tests.Screen;

/// <summary>
/// Color 值语义测试 — Default/Rgb/Ansi256 三态与相等性。
/// </summary>
public sealed class ColorTests
{
    [Fact]
    public void Default_DistinctFromBlackRgb()
    {
        Assert.NotEqual(Color.Default, Color.FromRgb(0, 0, 0));
    }

    [Fact]
    public void ClaudeOrange_IsRgb215_119_87()
    {
        var orange = Color.FromRgb(215, 119, 87);
        Assert.Equal(ColorKind.Rgb, orange.Kind);
        Assert.Equal((byte)215, orange.R);
        Assert.Equal((byte)119, orange.G);
        Assert.Equal((byte)87, orange.B);
    }

    [Fact]
    public void Ansi256_KindPreserved()
    {
        var c = Color.FromAnsi256(174);
        Assert.Equal(ColorKind.Ansi256, c.Kind);
    }

    [Fact]
    public void EqualRgb_Colors_AreEqual()
    {
        Assert.Equal(Color.FromRgb(1, 2, 3), Color.FromRgb(1, 2, 3));
    }
}

/// <summary>
/// StylePool intern 池测试 — 同样式同 id、transition 生成最优 SGR diff。
/// transition 顺序对齐基准 colorize.ts applyTextStyles：
/// inverse → strikethrough → underline → italic → bold → dim → fg → bg。
/// </summary>
public sealed class StylePoolTests
{
    [Fact]
    public void Intern_SameStyle_ReturnsSameId()
    {
        var pool = new StylePool();
        var a = pool.GetOrAdd(new TextStyle { Bold = true });
        var b = pool.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Intern_DifferentStyles_ReturnDifferentIds()
    {
        var pool = new StylePool();
        var a = pool.GetOrAdd(new TextStyle { Bold = true });
        var b = pool.GetOrAdd(new TextStyle { Italic = true });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DefaultStyle_PreRegisteredAsIdZero()
    {
        var pool = new StylePool();
        Assert.Equal(0, pool.GetOrAdd(TextStyle.Default));
        Assert.Equal(TextStyle.Default, pool.Get(0));
    }

    [Fact]
    public void Transition_SameStyle_EmptyString()
    {
        var pool = new StylePool();
        var id = pool.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(string.Empty, pool.Transition(id, id));
    }

    [Fact]
    public void Transition_DefaultToBold_EmitsBoldOnly()
    {
        var pool = new StylePool();
        var bold = pool.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal("\x1b[1m", pool.Transition(0, bold));
    }

    [Fact]
    public void Transition_BoldToBoldItalic_EmitsItalicOnly()
    {
        var pool = new StylePool();
        var bold = pool.GetOrAdd(new TextStyle { Bold = true });
        var boldItalic = pool.GetOrAdd(new TextStyle { Bold = true, Italic = true });
        Assert.Equal("\x1b[3m", pool.Transition(bold, boldItalic));
    }

    [Fact]
    public void Transition_FgChange_EmitsColorOnly()
    {
        var pool = new StylePool();
        var orange = pool.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(215, 119, 87) });
        var blue = pool.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(88, 166, 255) });
        Assert.Equal("\x1b[38;2;88;166;255m", pool.Transition(orange, blue));
    }

    [Fact]
    public void Transition_RgbToDefault_EmitsDefaultFg()
    {
        var pool = new StylePool();
        var orange = pool.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(215, 119, 87) });
        Assert.Equal("\x1b[39m", pool.Transition(orange, 0));
    }

    [Fact]
    public void Transition_BoldOffAndFgChange_EmitsResetBoldThenColor()
    {
        // 顺序: bold 关(22) 在 fg 之前
        var pool = new StylePool();
        var boldOrange = pool.GetOrAdd(new TextStyle { Bold = true, Foreground = Color.FromRgb(215, 119, 87) });
        var plainBlue = pool.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(88, 166, 255) });
        Assert.Equal("\x1b[22m\x1b[38;2;88;166;255m", pool.Transition(boldOrange, plainBlue));
    }

    [Fact]
    public void Transition_InverseEmittedFirst()
    {
        var pool = new StylePool();
        var plain = 0;
        var all = pool.GetOrAdd(new TextStyle
        {
            Inverse = true,
            Bold = true,
            Foreground = Color.FromRgb(215, 119, 87),
        });
        // inverse(7) → bold(1) → fg
        Assert.Equal("\x1b[7m\x1b[1m\x1b[38;2;215;119;87m", pool.Transition(plain, all));
    }

    [Fact]
    public void Transition_UnderlineOff_Emits24()
    {
        var pool = new StylePool();
        var u = pool.GetOrAdd(new TextStyle { Underline = true });
        Assert.Equal("\x1b[24m", pool.Transition(u, 0));
    }

    [Fact]
    public void Transition_BackgroundChange_EmittedAfterForeground()
    {
        var pool = new StylePool();
        var plain = 0;
        var both = pool.GetOrAdd(new TextStyle
        {
            Foreground = Color.FromRgb(215, 119, 87),
            Background = Color.FromRgb(55, 55, 55),
        });
        Assert.Equal("\x1b[38;2;215;119;87m\x1b[48;2;55;55;55m", pool.Transition(plain, both));
    }

    [Fact]
    public void Transition_Ansi256Fg_Emits38_5()
    {
        var pool = new StylePool();
        var c = pool.GetOrAdd(new TextStyle { Foreground = Color.FromAnsi256(174) });
        Assert.Equal("\x1b[38;5;174m", pool.Transition(0, c));
    }

    [Fact]
    public void Transition_CachedAcrossCalls()
    {
        var pool = new StylePool();
        var bold = pool.GetOrAdd(new TextStyle { Bold = true });
        var first = pool.Transition(0, bold);
        var second = pool.Transition(0, bold);
        Assert.Same(first, second);
    }
}
