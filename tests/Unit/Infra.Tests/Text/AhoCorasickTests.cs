namespace JoinCode.Infra.Tests.Text;

/// <summary>
/// Aho-Corasick 自动机单元测试。
/// </summary>
public class AhoCorasickTests
{
    [Fact]
    public void ContainsAny_EmptyText_ReturnsFalse()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("".AsSpan()).Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_NoMatch_ReturnsFalse()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("xyz").Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_SingleMatch_ReturnsTrue()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("he").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_MatchInMiddle_ReturnsTrue()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("ushers").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_IgnoreCase_MatchesUpperCase()
    {
        var ac = AhoCorasick.Create(["danger"], ignoreCase: true);
        ac.ContainsAny("This is DANGER").Should().BeTrue();
        ac.ContainsAny("This is Danger").Should().BeTrue();
        ac.ContainsAny("This is danger").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_OrdinalCase_DoesNotMatchUpperCase()
    {
        var ac = AhoCorasick.Create(["danger"], ignoreCase: false);
        ac.ContainsAny("This is DANGER").Should().BeFalse();
        ac.ContainsAny("This is danger").Should().BeTrue();
    }

    [Fact]
    public void FindAll_OverlappingPatterns_ReturnsAll()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        var matches = ac.FindAll("ushers".AsSpan());

        matches.Should().HaveCount(3);
        matches.Should().Contain(m => m.Value == "he" && m.StartIndex == 2);
        matches.Should().Contain(m => m.Value == "she" && m.StartIndex == 1);
        matches.Should().Contain(m => m.Value == "hers" && m.StartIndex == 2);
    }

    [Fact]
    public void FindAll_MultipleOccurrences_ReturnsAll()
    {
        var ac = AhoCorasick.Create(["a"]);
        var matches = ac.FindAll("banana".AsSpan());
        matches.Should().HaveCount(3);
        matches[0].StartIndex.Should().Be(1);
        matches[1].StartIndex.Should().Be(3);
        matches[2].StartIndex.Should().Be(5);
    }

    [Fact]
    public void FindAll_NoMatch_ReturnsEmpty()
    {
        var ac = AhoCorasick.Create(["xyz"]);
        var matches = ac.FindAll("hello".AsSpan());
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindFirst_ReturnsFirstMatch()
    {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        var match = ac.FindFirst("ushers".AsSpan());
        match.Should().NotBeNull();
        match!.Value.Value.Should().Be("she");
        match.Value.StartIndex.Should().Be(1);
    }

    [Fact]
    public void FindFirst_NoMatch_ReturnsNull()
    {
        var ac = AhoCorasick.Create(["xyz"]);
        ac.FindFirst("hello".AsSpan()).Should().BeNull();
    }

    [Fact]
    public void Create_EmptyPatterns_NeverMatches()
    {
        var ac = AhoCorasick.Create([]);
        ac.ContainsAny("anything").Should().BeFalse();
        ac.FindAll("anything".AsSpan()).Should().BeEmpty();
    }

    [Fact]
    public void Create_PatternWithAssociatedValue_ReturnsValue()
    {
        var ac = AhoCorasick<int>.Create([
            new("rm", 1),
            new("del", 2),
            new("format", 3),
        ]);

        var matches = ac.FindAll("execute del now".AsSpan());
        matches.Should().HaveCount(1);
        matches.Should().Contain(m => m.Value == 2 && m.StartIndex == 8);
    }

    [Fact]
    public void Create_PatternIsSubstringOfAnother_BothMatch()
    {
        var ac = AhoCorasick.Create(["he", "hello"]);
        var matches = ac.FindAll("hello".AsSpan());
        matches.Should().HaveCount(2);
        matches.Should().Contain(m => m.Value == "he" && m.StartIndex == 0);
        matches.Should().Contain(m => m.Value == "hello" && m.StartIndex == 0);
    }

    [Fact]
    public void ContainsAll_LargePatternSet_PerformanceSmoke()
    {
        var patterns = new List<string>(100);
        for (var i = 0; i < 100; i++)
            patterns.Add($"secret_{i}");

        var ac = AhoCorasick.Create(patterns);
        ac.ContainsAny("this contains secret_42 here").Should().BeTrue();
        ac.ContainsAny("this contains nothing here").Should().BeFalse();
    }

    [Fact]
    public void CreateBool_ReturnsTrueOnMatch()
    {
        var ac = AhoCorasick.CreateBool(["rm", "del", "format"]);
        var match = ac.FindFirst("execute del now".AsSpan());
        match.Should().NotBeNull();
        match!.Value.Value.Should().BeTrue();
    }
}

/// <summary>
/// 双缓冲 Aho-Corasick 自动机单元测试。
/// </summary>
public class DualBufferAhoCorasickTests
{
    [Fact]
    public void SwapPatterns_AtomicUpdate_NewPatternsTakeEffect()
    {
        var db = DualBufferAhoCorasick.Create(new[] { "old_pattern" });
        db.ContainsAny("old_pattern here").Should().BeTrue();
        db.ContainsAny("new_pattern here").Should().BeFalse();

        db.SwapPatterns(new[] { "new_pattern" }.Select(static p => new KeyValuePair<string, string>(p, p)));
        db.ContainsAny("old_pattern here").Should().BeFalse();
        db.ContainsAny("new_pattern here").Should().BeTrue();
    }

    [Fact]
    public void Current_AfterSwap_ReturnsNewAutomaton()
    {
        var db = DualBufferAhoCorasick.Create(new[] { "a" });
        var before = db.Current;
        db.SwapPatterns(new[] { "b" }.Select(static p => new KeyValuePair<string, string>(p, p)));
        var after = db.Current;
        before.Should().NotBeSameAs(after);
    }

    [Fact]
    public async Task ConcurrentReadDuringSwap_NoException()
    {
        var db = DualBufferAhoCorasick.Create(new[] { "initial" });

        var cts = new CancellationTokenSource();
        var readers = new Task[4];
        for (var i = 0; i < 4; i++)
        {
            readers[i] = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    db.ContainsAny("initial text");
                }
            });
        }

        for (var i = 0; i < 100; i++)
            db.SwapPatterns(new[] { $"pattern_{i}" }.Select(static p => new KeyValuePair<string, string>(p, p)));

        cts.Cancel();
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
