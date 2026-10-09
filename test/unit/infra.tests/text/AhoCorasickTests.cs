// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Infra.Tests.Text;

/// <summary>
/// Aho-Corasick 自动机单元测试。
/// </summary>
public class AhoCorasickTests {
    [Fact]
    public void ContainsAny_EmptyText_ReturnsFalse() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("".AsSpan()).Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_NoMatch_ReturnsFalse() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("xyz").Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_SingleMatch_ReturnsTrue() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("he").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_MatchInMiddle_ReturnsTrue() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        ac.ContainsAny("ushers").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_IgnoreCase_MatchesUpperCase() {
        var ac = AhoCorasick.Create(["danger"], ignoreCase: true);
        ac.ContainsAny("This is DANGER").Should().BeTrue();
        ac.ContainsAny("This is Danger").Should().BeTrue();
        ac.ContainsAny("This is danger").Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_OrdinalCase_DoesNotMatchUpperCase() {
        var ac = AhoCorasick.Create(["danger"], ignoreCase: false);
        ac.ContainsAny("This is DANGER").Should().BeFalse();
        ac.ContainsAny("This is danger").Should().BeTrue();
    }

    [Fact]
    public void FindAll_OverlappingPatterns_ReturnsAll() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        var matches = ac.FindAll("ushers".AsSpan());

        matches.Should().HaveCount(3);
        matches.Should().Contain(m => m.Value == "he" && m.StartIndex == 2);
        matches.Should().Contain(m => m.Value == "she" && m.StartIndex == 1);
        matches.Should().Contain(m => m.Value == "hers" && m.StartIndex == 2);
    }

    [Fact]
    public void FindAll_MultipleOccurrences_ReturnsAll() {
        var ac = AhoCorasick.Create(["a"]);
        var matches = ac.FindAll("banana".AsSpan());
        matches.Should().HaveCount(3);
        matches[0].StartIndex.Should().Be(1);
        matches[1].StartIndex.Should().Be(3);
        matches[2].StartIndex.Should().Be(5);
    }

    [Fact]
    public void FindAll_NoMatch_ReturnsEmpty() {
        var ac = AhoCorasick.Create(["xyz"]);
        var matches = ac.FindAll("hello".AsSpan());
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindFirst_ReturnsFirstMatch() {
        var ac = AhoCorasick.Create(["he", "she", "his", "hers"]);
        var match = ac.FindFirst("ushers".AsSpan());
        match.Should().NotBeNull();
        match!.Value.Value.Should().Be("she");
        match.Value.StartIndex.Should().Be(1);
    }

    [Fact]
    public void FindFirst_NoMatch_ReturnsNull() {
        var ac = AhoCorasick.Create(["xyz"]);
        ac.FindFirst("hello".AsSpan()).Should().BeNull();
    }

    [Fact]
    public void Create_EmptyPatterns_NeverMatches() {
        var ac = AhoCorasick.Create([]);
        ac.ContainsAny("anything").Should().BeFalse();
        ac.FindAll("anything".AsSpan()).Should().BeEmpty();
    }

    [Fact]
    public void Create_PatternWithAssociatedValue_ReturnsValue() {
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
    public void Create_PatternIsSubstringOfAnother_BothMatch() {
        var ac = AhoCorasick.Create(["he", "hello"]);
        var matches = ac.FindAll("hello".AsSpan());
        matches.Should().HaveCount(2);
        matches.Should().Contain(m => m.Value == "he" && m.StartIndex == 0);
        matches.Should().Contain(m => m.Value == "hello" && m.StartIndex == 0);
    }

    [Fact]
    public void ContainsAll_LargePatternSet_PerformanceSmoke() {
        var patterns = new List<string>(100);
        for (var i = 0; i < 100; i++)
            patterns.Add($"secret_{i}");

        var ac = AhoCorasick.Create(patterns);
        ac.ContainsAny("this contains secret_42 here").Should().BeTrue();
        ac.ContainsAny("this contains nothing here").Should().BeFalse();
    }

    [Fact]
    public void CreateBool_ReturnsTrueOnMatch() {
        var ac = AhoCorasick.CreateBool(["rm", "del", "format"]);
        var match = ac.FindFirst("execute del now".AsSpan());
        match.Should().NotBeNull();
        match!.Value.Value.Should().BeTrue();
    }

    // ===== BuildGotoFunction 确定性测试(阶段2.9 拆分) =====

    [Fact]
    public void BuildGotoFunction_SinglePattern_CreatesLinearTrie() {
        var patterns = new List<KeyValuePair<string, string>> { new("abc", "abc") };
        var (transitions, outputs, hasPattern) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: true);

        hasPattern.Should().BeTrue();
        // 0 -a-> 1 -b-> 2 -c-> 3
        transitions.Should().HaveCount(4);
        outputs.Should().HaveCount(4);
        transitions[0].Should().ContainKey('a');
        transitions[1].Should().ContainKey('b');
        transitions[2].Should().ContainKey('c');
        outputs[3].Should().ContainSingle(o => o.Length == 3 && o.Value == "abc");
    }

    [Fact]
    public void BuildGotoFunction_SharedPrefix_SharesStates() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("her", "her")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);

        // 0 -h-> 1 -e-> 2 (he) -r-> 3 (her)
        transitions.Should().HaveCount(4);
        transitions[0]['h'].Should().Be(1);
        transitions[1]['e'].Should().Be(2);
        transitions[2]['r'].Should().Be(3);
        outputs[2].Should().ContainSingle(o => o.Length == 2);
        outputs[3].Should().ContainSingle(o => o.Length == 3);
    }

    [Fact]
    public void BuildGotoFunction_EmptyPatternStrings_Skipped_HasPatternFalse() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("", "v1"), new("", "v2")
        };
        var (transitions, outputs, hasPattern) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: true);

        hasPattern.Should().BeFalse();
        transitions.Should().ContainSingle(); // 仅根节点
        outputs.Should().ContainSingle();
    }

    [Fact]
    public void BuildGotoFunction_EmptyList_HasPatternFalse() {
        var (transitions, _, hasPattern) = AhoCorasick<string>.BuildGotoFunction(
            Array.Empty<KeyValuePair<string, string>>(), ignoreCase: true);

        hasPattern.Should().BeFalse();
        transitions.Should().ContainSingle();
    }

    [Fact]
    public void BuildGotoFunction_IgnoreCase_LowercasesChars() {
        var patterns = new List<KeyValuePair<string, string>> { new("HE", "HE") };
        var (transitions, _, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: true);

        transitions[0].Should().ContainKey('h');
        transitions[0].Should().NotContainKey('H');
    }

    [Fact]
    public void BuildGotoFunction_OrdinalCase_PreservesChars() {
        var patterns = new List<KeyValuePair<string, string>> { new("HE", "HE") };
        var (transitions, _, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);

        transitions[0].Should().ContainKey('H');
        transitions[0].Should().NotContainKey('h');
    }

    [Fact]
    public void BuildGotoFunction_MultiplePatterns_OutputsAtCorrectStates() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("she", "she")
        };
        var (transitions, outputs, hasPattern) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);

        hasPattern.Should().BeTrue();
        // 0 -h-> 1 -e-> 2 (he), 0 -s-> 3 -h-> 4 -e-> 5 (she)
        transitions.Should().HaveCount(6);
        outputs[2].Should().ContainSingle(o => o.Length == 2 && o.Value == "he");
        outputs[5].Should().ContainSingle(o => o.Length == 3 && o.Value == "she");
    }

    // ===== BuildFailureFunction 确定性测试(阶段2.9 拆分) =====

    [Fact]
    public void BuildFailureFunction_SingleCharPattern_AllFailuresZero() {
        var patterns = new List<KeyValuePair<string, string>> { new("a", "a") };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        var failures = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        failures.Should().Equal([0, 0]);
    }

    [Fact]
    public void BuildFailureFunction_RootFailureIsAlwaysZero() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("she", "she"), new("his", "his"), new("hers", "hers")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        var failures = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        failures[0].Should().Be(0);
    }

    [Fact]
    public void BuildFailureFunction_ClassicExample_HESheHisHers_FailureChainCorrect() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("she", "she"), new("his", "his"), new("hers", "hers")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        var failures = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        // 状态: 0(root) 1(h) 2(he) 3(s) 4(sh) 5(she) 6(hi) 7(his) 8(her) 9(hers)
        // failures = [0, 0, 0, 0, 1, 2, 0, 3, 0, 3]
        failures.Should().Equal([0, 0, 0, 0, 1, 2, 0, 3, 0, 3]);
    }

    [Fact]
    public void BuildFailureFunction_OutputChainMerged_SheStateContainsHe() {
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("she", "she"), new("his", "his"), new("hers", "hers")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        _ = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        // she 状态(5) 的 failure=2(he),输出链合并后 outputs[5] 应同时包含 she(len=3) 和 he(len=2)
        outputs[5].Should().HaveCount(2);
        outputs[5].Should().Contain(o => o.Length == 3 && o.Value == "she");
        outputs[5].Should().Contain(o => o.Length == 2 && o.Value == "he");
    }

    [Fact]
    public void BuildFailureFunction_NoSharedSuffix_AllFailuresZero() {
        // 无共享后缀:每个非根状态的 failure 都回退到 0
        var patterns = new List<KeyValuePair<string, string>> {
            new("ab", "ab"), new("cd", "cd")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        var failures = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        // 0 -a-> 1 -b-> 2, 0 -c-> 3 -d-> 4
        // failures = [0, 0, 0, 0, 0]
        failures.Should().Equal([0, 0, 0, 0, 0]);
    }

    [Fact]
    public void BuildFailureFunction_PrefixRelationship_HeAndHello() {
        // "he" 是 "hello" 的前缀,但 failure 链按后缀走
        var patterns = new List<KeyValuePair<string, string>> {
            new("he", "he"), new("hello", "hello")
        };
        var (transitions, outputs, _) = AhoCorasick<string>.BuildGotoFunction(patterns, ignoreCase: false);
        var failures = AhoCorasick<string>.BuildFailureFunction(transitions, outputs);

        // 0 -h-> 1 -e-> 2 (he) -l-> 3 -l-> 4 -o-> 5 (hello)
        // failures: 1(h)=0, 2(he)=0, 3(hel)=0, 4(hell)=0, 5(hello)=0
        // (无任何后缀能回退到另一个模式的前缀)
        failures.Should().Equal([0, 0, 0, 0, 0, 0]);
        outputs[2].Should().ContainSingle(o => o.Length == 2);
        outputs[5].Should().ContainSingle(o => o.Length == 5);
    }
}

/// <summary>
/// 双缓冲 Aho-Corasick 自动机单元测试。
/// </summary>
public class DualBufferAhoCorasickTests {
    [Fact]
    public void SwapPatterns_AtomicUpdate_NewPatternsTakeEffect() {
        var db = DualBufferAhoCorasick.Create(new[] { "old_pattern" });
        db.ContainsAny("old_pattern here").Should().BeTrue();
        db.ContainsAny("new_pattern here").Should().BeFalse();

        db.SwapPatterns(new[] { "new_pattern" }.Select(static p => new KeyValuePair<string, string>(p, p)));
        db.ContainsAny("old_pattern here").Should().BeFalse();
        db.ContainsAny("new_pattern here").Should().BeTrue();
    }

    [Fact]
    public void Current_AfterSwap_ReturnsNewAutomaton() {
        var db = DualBufferAhoCorasick.Create(new[] { "a" });
        var before = db.Current;
        db.SwapPatterns(new[] { "b" }.Select(static p => new KeyValuePair<string, string>(p, p)));
        var after = db.Current;
        before.Should().NotBeSameAs(after);
    }

    [Fact]
    public async Task ConcurrentReadDuringSwap_NoException() {
        var db = DualBufferAhoCorasick.Create(new[] { "initial" });

        using var cts = new CancellationTokenSource();
        var readers = new Task[4];
        for (var i = 0; i < 4; i++) {
            readers[i] = Task.Run(() => {
                while (!cts.IsCancellationRequested) {
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