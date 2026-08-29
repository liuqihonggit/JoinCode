namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// SlashCommandRanker 单元测试 — 验证完全匹配优先、权重前置、长度升序、字母序兜底。
/// </summary>
public class SlashCommandRankerTests
{
    private static SlashCommandItem Item(string name) =>
        new() { Name = name, Description = "" };

    [Fact]
    public void Rank_ExactMatch_GoesFirst()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/apple"),
            Item("/ap"),
            Item("/apply")
        };

        var ranked = SlashCommandRanker.Rank(candidates, "/ap");

        ranked[0].Name.Should().Be("/ap");
    }

    [Fact]
    public void Rank_ShorterName_BeforeLonger_WhenNoWeight()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/application"),
            Item("/api"),
            Item("/apple")
        };

        var ranked = SlashCommandRanker.Rank(candidates, "/a");

        ranked.Select(c => c.Name).Should().BeEquivalentTo(["/api", "/apple", "/application"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Rank_Weight_PromotesHigherWeight()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/apple"),
            Item("/apply")
        };
        var weights = new Dictionary<string, int> { ["/apple"] = 10, ["/apply"] = 1 };

        var ranked = SlashCommandRanker.Rank(candidates, "/ap", weights);

        ranked[0].Name.Should().Be("/apple");
    }

    [Fact]
    public void Rank_ExactMatch_BeatsWeight()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/apple"),
            Item("/ap")
        };
        var weights = new Dictionary<string, int> { ["/apple"] = 100 };

        var ranked = SlashCommandRanker.Rank(candidates, "/ap", weights);

        ranked[0].Name.Should().Be("/ap");
    }

    [Fact]
    public void Rank_EmptyCandidates_ReturnsEmpty()
    {
        var ranked = SlashCommandRanker.Rank(Array.Empty<SlashCommandItem>(), "/a");
        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Rank_SingleCandidate_ReturnsAsIs()
    {
        var single = Item("/apple");
        var ranked = SlashCommandRanker.Rank(new[] { single }, "/a");
        ranked.Should().HaveCount(1);
        ranked[0].Should().BeSameAs(single);
    }

    [Fact]
    public void Rank_AlphabeticalOrder_AsTiebreaker()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/banana"),
            Item("/apple"),
            Item("/cherry")
        };

        var ranked = SlashCommandRanker.Rank(candidates, "/");

        ranked.Select(c => c.Name).Should().BeEquivalentTo(["/apple", "/banana", "/cherry"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Rank_NoWeights_FallsBackToLengthThenAlpha()
    {
        var candidates = new List<SlashCommandItem>
        {
            Item("/copy"),
            Item("/clear"),
            Item("/compact"),
            Item("/config")
        };

        var ranked = SlashCommandRanker.Rank(candidates, "/c");

        ranked.Select(c => c.Name).Should().BeEquivalentTo(["/copy", "/clear", "/config", "/compact"], o => o.WithStrictOrdering());
    }
}
