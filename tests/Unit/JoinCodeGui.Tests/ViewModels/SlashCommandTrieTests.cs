using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// SlashCommandTrie 前缀树测试 — 验证前缀匹配（/a /ap → /apple）与空前缀返回全部。
/// </summary>
public sealed class SlashCommandTrieTests
{
    private static SlashCommandTrie BuildTrie()
    {
        return new SlashCommandTrie(
        [
            new SlashCommandItem { Name = "/apple", Description = "苹果" },
            new SlashCommandItem { Name = "/apricot", Description = "杏" },
            new SlashCommandItem { Name = "/banana", Description = "香蕉" }
        ]);
    }

    [Fact]
    public void Match_PrefixA_ReturnsApple()
    {
        var trie = BuildTrie();

        var result = trie.Match("/a");

        result.Should().Contain(c => c.Name == "/apple");
        result.Should().Contain(c => c.Name == "/apricot");
        result.Should().NotContain(c => c.Name == "/banana");
    }

    [Fact]
    public void Match_PrefixAp_ReturnsApple()
    {
        var trie = BuildTrie();

        var result = trie.Match("/ap");

        result.Should().Contain(c => c.Name == "/apple");
        result.Should().Contain(c => c.Name == "/apricot");
        result.Should().OnlyContain(c => c.Name == "/apple" || c.Name == "/apricot");
    }

    [Fact]
    public void Match_PrefixApple_UniquelyIdentifiesApple()
    {
        var trie = BuildTrie();

        var result = trie.Match("/apple");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("/apple");
    }

    [Fact]
    public void Match_PrefixEmpty_ReturnsAll()
    {
        var trie = BuildTrie();

        var result = trie.Match("");

        result.Should().HaveCount(3);
    }

    [Fact]
    public void Match_PrefixNoMatch_ReturnsEmpty()
    {
        var trie = BuildTrie();

        var result = trie.Match("/zzz");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var trie = BuildTrie();

        var result = trie.Match("/APPLE");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("/apple");
    }
}
