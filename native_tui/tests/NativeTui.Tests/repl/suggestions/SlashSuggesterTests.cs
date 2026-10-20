namespace NativeTui.Tests.Repl.Suggestions;

/// <summary>
/// SlashSuggester 单元测试 — 对齐基准 commandSuggestions.ts 过滤排序：
/// 触发（/ 开头、无空格、非抑制）；空查询全量可见字母序；
/// 二级排序（精确名 &gt; 精确切名 &gt; 前缀名短优先 &gt; 前缀切名 &gt; 子序列）；
/// 隐藏命令全名前插；Esc 抑制同输入；选中循环导航。
/// </summary>
public sealed class SlashSuggesterTests
{
    /// <summary>标准命令集（含别名/隐藏命令，覆盖各排序层级）。</summary>
    private static List<CommandSuggestion> Catalog() =>
    [
        new("help", "显示帮助", [], IsHidden: false),
        new("hello", "问候", [], IsHidden: false),
        new("help-me", "求助", ["hm"], IsHidden: false),
        new("compact", "压缩上下文", [], IsHidden: false),
        new("secret", "隐藏命令", [], IsHidden: true),
    ];

    /// <summary>构造 suggester 并指定输入。</summary>
    private static SlashSuggester Suggester(string input)
    {
        var suggester = new SlashSuggester(Catalog());
        suggester.Update(input);
        return suggester;
    }

    /// <summary>空查询（仅 /）：全量可见命令，字母序（hello &lt; help）。</summary>
    [Fact]
    public void Update_SlashOnly_AllVisibleAlphabetical()
    {
        var suggester = Suggester("/");
        Assert.Equal(["compact", "hello", "help", "help-me"], suggester.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>隐藏命令默认不参与建议。</summary>
    [Fact]
    public void Update_Hidden_Excluded()
    {
        var suggester = Suggester("/");
        Assert.DoesNotContain(suggester.Items, i => i.Name == "secret");
    }

    /// <summary>前缀匹配：/he 命中 help/hello/help-me（按层级排序）。</summary>
    [Fact]
    public void Update_PrefixQuery_MatchesPrefixCommands()
    {
        var suggester = Suggester("/he");
        Assert.Equal(["help", "hello", "help-me"], suggester.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>精确名优先：/help 排在 help-me 与 hello 之前。</summary>
    [Fact]
    public void Update_ExactName_RanksFirst()
    {
        var suggester = Suggester("/help");
        Assert.Equal("help", suggester.Items[0].Name);
    }

    /// <summary>精确切名优先于前缀名：/hm 是 help-me 的别名 → 排第一。</summary>
    [Fact]
    public void Update_ExactAlias_RanksBeforePrefixNames()
    {
        var suggester = Suggester("/hm");
        Assert.Equal("help-me", suggester.Items[0].Name);
    }

    /// <summary>同为前缀名：短命令优先（/he → help(4) 在 hello(5) 前）。</summary>
    [Fact]
    public void Update_PrefixTie_ShorterFirst()
    {
        var suggester = Suggester("/he");
        Assert.Equal("help", suggester.Items[0].Name);
        Assert.Equal("hello", suggester.Items[1].Name);
    }

    /// <summary>子序列匹配（Fuse 简化）：/cmp 命中 compact。</summary>
    [Fact]
    public void Update_Subsequence_Matches()
    {
        var suggester = Suggester("/cmp");
        Assert.Equal(["compact"], suggester.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>子序列名（tier4）优先于子序列切名（tier5）。</summary>
    [Fact]
    public void Update_SubsequenceName_BeforeSubsequenceAlias()
    {
        var commands = new List<CommandSuggestion>
        {
            new("gamma", "描述", ["xalp"], IsHidden: false),
            new("alphabet", "描述", [], IsHidden: false),
        };
        var suggester = new SlashSuggester(commands);
        suggester.Update("/alp");
        Assert.Equal(["alphabet", "gamma"], suggester.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>无匹配：Items 空且不激活。</summary>
    [Fact]
    public void Update_NoMatch_Inactive()
    {
        var suggester = Suggester("/zzz");
        Assert.Empty(suggester.Items);
        Assert.False(suggester.IsActive);
    }

    /// <summary>非 / 开头：不触发。</summary>
    [Fact]
    public void Update_NotSlash_Prefix_Inactive()
    {
        var suggester = Suggester("plain text");
        Assert.False(suggester.IsActive);
    }

    /// <summary>命令后带空格（参数态）：不触发。</summary>
    [Fact]
    public void Update_SlashWithSpace_Inactive()
    {
        var suggester = Suggester("/help me");
        Assert.False(suggester.IsActive);
    }

    /// <summary>空输入：不触发。</summary>
    [Fact]
    public void Update_Empty_Inactive()
    {
        var suggester = Suggester(string.Empty);
        Assert.False(suggester.IsActive);
    }

    /// <summary>激活态：有匹配项即激活，选中默认首项（基准 commandItems 首项预选）。</summary>
    [Fact]
    public void Update_Match_ActiveWithDefaultSelection()
    {
        var suggester = Suggester("/he");
        Assert.True(suggester.IsActive);
        Assert.Equal(0, suggester.SelectedIndex);
        Assert.Equal("help", suggester.Selected?.Name);
    }

    /// <summary>隐藏命令全名：前插首位（精确名永远赢过隐藏过滤）。</summary>
    [Fact]
    public void Update_HiddenFullName_Prepended()
    {
        var suggester = Suggester("/secret");
        Assert.Equal("secret", suggester.Items[0].Name);
    }

    /// <summary>Esc 抑制：Dismiss 后同一输入不再触发。</summary>
    [Fact]
    public void Dismiss_SuppressesSameInput()
    {
        var suggester = Suggester("/he");
        Assert.True(suggester.IsActive);
        suggester.Dismiss();
        Assert.False(suggester.IsActive);

        suggester.Update("/he");
        Assert.False(suggester.IsActive);
    }

    /// <summary>Esc 抑制解除：输入变化后重新触发。</summary>
    [Fact]
    public void Dismiss_InputChanges_Retriggers()
    {
        var suggester = Suggester("/he");
        suggester.Dismiss();
        suggester.Update("/hel");
        Assert.True(suggester.IsActive);
    }

    /// <summary>导航循环：末项 +1 回绕到 0（基准 autocomplete 循环语义）。</summary>
    [Fact]
    public void Move_WrapsAround()
    {
        var suggester = Suggester("/he");
        suggester.Move(1);
        Assert.Equal(1, suggester.SelectedIndex);
        suggester.Move(1);
        Assert.Equal(2, suggester.SelectedIndex);
        suggester.Move(1);
        Assert.Equal(0, suggester.SelectedIndex);
    }

    /// <summary>导航循环：首项 -1 回绕到末项。</summary>
    [Fact]
    public void Move_Backward_WrapsToLast()
    {
        var suggester = Suggester("/he");
        suggester.Move(-1);
        Assert.Equal(2, suggester.SelectedIndex);
    }

    /// <summary>列表更新后选中回零（简单回退，基准按 id 找回的简化）。</summary>
    [Fact]
    public void Update_NewQuery_ResetsSelection()
    {
        var suggester = Suggester("/he");
        suggester.Move(1);
        suggester.Update("/hel");
        Assert.Equal(0, suggester.SelectedIndex);
    }

    /// <summary>大小写不敏感：/HE 命中 help。</summary>
    [Fact]
    public void Update_CaseInsensitive()
    {
        var suggester = Suggester("/HE");
        Assert.Equal(["help", "hello", "help-me"], suggester.Items.Select(i => i.Name).ToArray());
    }
}
