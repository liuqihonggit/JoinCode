namespace Brain.Tests.Context.Compact.Guard;

[Trait("Category", "Unit")]
public class GibberishDetectorTests
{
    [Fact]
    public void Detect_RandomCharacters_ReturnsGibberish()
    {
        var random = new Random(42);
        var chars = new char[500];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (char)random.Next(33, 127);
        var gibberish = new string(chars);

        var result = GibberishDetector.Detect(gibberish);

        result.IsGibberish.Should().BeTrue();
    }

    [Fact]
    public void Detect_NormalEnglishText_ReturnsClean()
    {
        var text = "This is a normal summary of the conversation. The user asked about implementing " +
                   "a new feature and the assistant provided a detailed response with code examples.";

        var result = GibberishDetector.Detect(text);

        result.IsGibberish.Should().BeFalse();
    }

    [Fact]
    public void Detect_NormalChineseText_ReturnsClean()
    {
        var text = "用户询问了如何实现新功能，助手提供了详细的代码示例和解释。" +
                   "讨论了多种实现方案，最终选择了最简单的方式。";

        var result = GibberishDetector.Detect(text);

        result.IsGibberish.Should().BeFalse();
    }

    [Fact]
    public void Detect_ShortText_SkipsDetection()
    {
        var result = GibberishDetector.Detect("hi");

        result.IsGibberish.Should().BeFalse();
        result.Skipped.Should().BeTrue();
    }

    [Fact]
    public void Detect_EmptyString_SkipsDetection()
    {
        var result = GibberishDetector.Detect("");

        result.IsGibberish.Should().BeFalse();
        result.Skipped.Should().BeTrue();
    }

    [Fact]
    public void Detect_RepeatedSingleChar_ReturnsRepetition()
    {
        var text = new string('a', 500);

        var result = GibberishDetector.Detect(text);

        result.IsRepetition.Should().BeTrue();
    }

    [Fact]
    public void Detect_MixedNormalAndGibberish_ReturnsClean()
    {
        var text = "Normal text here: " + new string('x', 50) + " and more normal text after.";

        var result = GibberishDetector.Detect(text);

        result.IsGibberish.Should().BeFalse();
    }
}
