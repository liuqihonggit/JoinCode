using FluentAssertions;

using JoinCode.Gui.SlashCommands;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// SlashCommandTrie 单元测试 — 验证前缀匹配、大小写不敏感、动态增删、边界行为。
/// </summary>
public class SlashCommandTrieTests
{
    private static SlashCommandItem Item(string name, string desc = "") =>
        new() { Name = name, Description = desc };

    [Fact]
    public void Search_ByPrefix_ReturnsMatchingCommands()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/apply"));
        trie.Insert(Item("/banana"));

        var results = trie.Search("/ap");
        results.Should().HaveCount(2);
        results.Select(r => r.Name).Should().BeEquivalentTo(["/apple", "/apply"]);
    }

    [Fact]
    public void Search_CaseInsensitive_MatchesButPreservesOriginalCase()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/Apple"));

        var results = trie.Search("/a");
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("/Apple");
    }

    [Fact]
    public void Search_UpperCasePrefix_MatchesLowerCaseCommand()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));

        trie.Search("/AP").Should().HaveCount(1);
        trie.Search("/Ap").Should().HaveCount(1);
    }

    [Fact]
    public void Search_EmptyPrefix_ReturnsAll()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/banana"));

        trie.Search("").Should().HaveCount(2);
    }

    [Fact]
    public void Search_OnlySlash_ReturnsAll()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/banana"));

        trie.Search("/").Should().HaveCount(2);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));

        trie.Search("/xyz").Should().BeEmpty();
    }

    [Fact]
    public void Search_PrefixWithoutSlash_AlsoMatches()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));

        trie.Search("ap").Should().HaveCount(1);
        trie.Search("/ap").Should().HaveCount(1);
    }

    [Fact]
    public void Insert_DuplicateName_Overwrites()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple", "old"));
        trie.Insert(Item("/apple", "new"));

        var results = trie.Search("/apple");
        results.Should().HaveCount(1);
        results[0].Description.Should().Be("new");
    }

    [Fact]
    public void Insert_CaseVariantName_TreatedAsSame()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/Apple", "first"));
        trie.Insert(Item("/apple", "second"));

        trie.Count.Should().Be(1);
        trie.Search("/apple")[0].Description.Should().Be("second");
    }

    [Fact]
    public void Remove_EliminatesCommand_FromSearch()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/apply"));

        trie.Remove("/apple").Should().BeTrue();
        var results = trie.Search("/ap");
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("/apply");
    }

    [Fact]
    public void Remove_NonExisting_ReturnsFalse()
    {
        var trie = new SlashCommandTrie();
        trie.Remove("/nope").Should().BeFalse();
    }

    [Fact]
    public void Remove_CleansUpIntermediateNodes()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/abc"));
        trie.Insert(Item("/abd"));

        trie.Remove("/abc").Should().BeTrue();
        trie.Search("/ab").Should().HaveCount(1);
        trie.Search("/abc").Should().BeEmpty();
        trie.Search("/abd").Should().HaveCount(1);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/banana"));

        trie.Clear();
        trie.Search("").Should().BeEmpty();
        trie.Count.Should().Be(0);
    }

    [Fact]
    public void Count_TracksInsertedCommands()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/apple"));
        trie.Insert(Item("/banana"));
        trie.Count.Should().Be(2);
        trie.Remove("/apple");
        trie.Count.Should().Be(1);
    }

    [Fact]
    public void InsertRange_BatchInsertsAll()
    {
        var trie = new SlashCommandTrie();
        trie.InsertRange([Item("/apple"), Item("/banana"), Item("/cherry")]);

        trie.Count.Should().Be(3);
        trie.Search("").Should().HaveCount(3);
    }

    [Fact]
    public void Search_SingleCharacterPrefix_MatchesAllStartingWithThatChar()
    {
        var trie = new SlashCommandTrie();
        trie.Insert(Item("/clear"));
        trie.Insert(Item("/compact"));
        trie.Insert(Item("/copy"));
        trie.Insert(Item("/config"));
        trie.Insert(Item("/model"));

        var results = trie.Search("/c");
        results.Should().HaveCount(4);
        results.Select(r => r.Name).Should().NotContain("/model");
    }
}
