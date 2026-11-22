namespace Composition.Tests.Commands;

/// <summary>
/// 选择器关键字过滤测试（TASK069-C2 — 兑现 Selector 构造的 enableSearch 参数）：
/// 大小写不敏感子串、多词 AND、空白关键字引用原数组（零分配快路径）。
/// </summary>
public sealed class SelectorFilterTests {
    private static string Key((string Item, string DisplayText, string SearchKey) entry) => entry.SearchKey;

    private static (string Item, string DisplayText, string SearchKey)[] Items() =>
    [
        ("s1", "[8f2a1c9d...] fix login", "8f2a1c9d fix login"),
        ("s2", "[33bb77aa...] refactor parser", "33bb77aa refactor parser"),
        ("s3", "[aa11bb22...] Login retry", "aa11bb22 Login retry"),
    ];

    [Fact]
    public void BlankKeyword_ReturnsSameInstance() {
        var items = Items();

        Assert.Same(items, JoinCode.Cli.SelectorFilter.Apply(items, Key, null));
        Assert.Same(items, JoinCode.Cli.SelectorFilter.Apply(items, Key, "   "));
    }

    [Fact]
    public void Keyword_IsCaseInsensitive() {
        var result = JoinCode.Cli.SelectorFilter.Apply(Items(), Key, "LOGIN");

        Assert.Equal(["s1", "s3"], result.Select(static entry => entry.Item));
    }

    [Fact]
    public void MultipleTokens_AllMustMatch() {
        // 「login fix」跨两段命中同一项（AND 语义）
        var result = JoinCode.Cli.SelectorFilter.Apply(Items(), Key, "login fix");

        Assert.Equal(["s1"], result.Select(static entry => entry.Item));
    }

    [Fact]
    public void Keyword_MatchesSessionId() {
        var result = JoinCode.Cli.SelectorFilter.Apply(Items(), Key, "33bb");

        Assert.Equal(["s2"], result.Select(static entry => entry.Item));
    }

    [Fact]
    public void NoMatch_ReturnsEmpty() {
        Assert.Empty(JoinCode.Cli.SelectorFilter.Apply(Items(), Key, "zzz"));
    }
}
