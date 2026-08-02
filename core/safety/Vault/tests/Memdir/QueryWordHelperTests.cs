
namespace Core.Tests.Memdir;

public sealed class QueryWordHelperTests
{
    [Theory]
    [InlineData("hello world", 2)]
    [InlineData("one,two;three", 3)]
    [InlineData("  spaced   text  ", 2)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a!b?c", 3)]
    public void ExtractWords_ReturnsExpectedCount(string input, int expectedCount)
    {
        var words = QueryWordHelper.ExtractWords(input);

        words.Should().HaveCount(expectedCount);
    }

    [Theory]
    [InlineData("hello world", "hello", true)]
    [InlineData("Hello World", "HELLO", true)]
    [InlineData("hello world", "xyz", false)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    [InlineData("prefix hello suffix", "hello", true)]
    public void ContainsOrdinalIgnoreCase_ReturnsExpected(string source, string value, bool expected)
    {
        var result = QueryWordHelper.ContainsOrdinalIgnoreCase(source.AsSpan(), value.AsSpan());

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world", "hello", true)]
    [InlineData("hello world", "world", true)]
    [InlineData("hello world", "hello world", true)]
    [InlineData("hello world", "hell", false)]
    [InlineData("hello world", "worlds", false)]
    [InlineData("hello world", "", false)]
    [InlineData("", "hello", false)]
    [InlineData("say hello world today", "hello", true)]
    [InlineData("say hello-world today", "hello", false)]
    public void ContainsWholeWordOrdinalIgnoreCase_ReturnsExpected(string source, string word, bool expected)
    {
        var result = QueryWordHelper.ContainsWholeWordOrdinalIgnoreCase(source.AsSpan(), word.AsSpan());

        result.Should().Be(expected);
    }

    [Fact]
    public void ExtractWords_WithMinLength_FiltersShortWords()
    {
        var words = QueryWordHelper.ExtractWords("a big cat", minLength: 2);

        words.Should().BeEquivalentTo(new[] { "big", "cat" });
    }

    [Fact]
    public void ExtractQueryWords_SplitsOnSeparators()
    {
        var words = QueryWordHelper.ExtractQueryWords("alpha, beta;gamma".AsSpan());

        words.Should().BeEquivalentTo(new[] { "alpha", "beta", "gamma" });
    }

    [Fact]
    public void ExtractQueryWords_EmptyOrWhiteSpace_ReturnsEmpty()
    {
        QueryWordHelper.ExtractQueryWords(ReadOnlySpan<char>.Empty).Should().BeEmpty();
        QueryWordHelper.ExtractQueryWords("   ".AsSpan()).Should().BeEmpty();
    }
}
