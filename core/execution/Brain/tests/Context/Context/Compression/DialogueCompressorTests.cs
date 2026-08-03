
namespace Core.Tests.Context.Compression;

public class DialogueCompressorTests
{
    private readonly DialogueCompressor _compressor = new();

    [Fact]
    public void Name_ShouldReturnCorrectValue()
    {
        _compressor.Name.Should().Be("DialogueCompressor");
    }

    [Fact]
    public void SupportedContentTypes_ShouldContainDialogue()
    {
        _compressor.SupportedContentTypes.Should().Contain(ContentType.Dialogue);
    }

    [Fact]
    public void CanHandle_DialogueContent_ShouldReturnTrue()
    {
        var dialogue = @"User: Hello, how are you today?
Assistant: Hi there! I'm doing great, thanks for asking.
User: Can you help me with a programming question?
Assistant: Of course! I'd be happy to help. What would you like to know?";
        _compressor.CanHandle(dialogue, ContentType.Dialogue).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonDialogueContent_ShouldReturnFalse()
    {
        var content = "public class Test { }";
        _compressor.CanHandle(content, ContentType.Code).Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_EmptyContent_ShouldReturnEmpty()
    {
        var result = await _compressor.CompressAsync("", CompressionOptions.Default).ConfigureAwait(true);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompressAsync_ShortDialogue_ShouldNotCompress()
    {
        var dialogue = @"User: Hello
Assistant: Hi!";

        var result = await _compressor.CompressAsync(dialogue, CompressionOptions.ForDialogue).ConfigureAwait(true);

        result.Should().Be(dialogue);
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveRecentMessages()
    {
        var dialogue = @"User: Question 1
Assistant: Answer 1
User: Question 2
Assistant: Answer 2
User: Question 3
Assistant: Answer 3
User: Question 4
Assistant: Answer 4";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 2,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().Contain("Question 3");
        result.Should().Contain("Answer 3");
        result.Should().Contain("Question 4");
        result.Should().Contain("Answer 4");
    }

    [Fact]
    public async Task CompressAsync_ShouldSummarizeOldMessages()
    {
        var dialogue = @"User: Question 1
Assistant: Answer 1
User: Question 2
Assistant: Answer 2
User: Question 3
Assistant: Answer 3";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 1,
            UseSummarization = true,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().Contain("对话摘要");
    }

    [Fact]
    public async Task CompressAsync_ShouldExtractKeyDecisions()
    {
        var dialogue = @"User: Question 1
Assistant: Answer 1. 决定：使用方案A。
User: Question 2
Assistant: Answer 2";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 1,
            PreserveKeyDecisions = true,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().Contain("关键决策");
        result.Should().Contain("使用方案A");
    }

    [Fact]
    public async Task CompressAsync_WithDifferentMessageFormats_ShouldHandleCorrectly()
    {
        var dialogue = @"Human: Hello
AI: Hi!
Human: How are you?
AI: I'm fine!";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 1,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().Contain("最近对话");
    }

    [Fact]
    public void EstimateCompressionRatio_LongDialogue_ShouldReturnLowerRatio()
    {
        // 创建足够多的对话轮次（超过 DialogueRoundsToPreserve=2）
        // 需要至少3个完整轮次（User+Assistant）才能触发压缩
        var dialogueLines = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            dialogueLines.Add($"User: Question {i}");
            dialogueLines.Add($"Assistant: Answer {i} with some detailed explanation and code examples");
        }
        var dialogue = string.Join("\n", dialogueLines);

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 2,
            UseSummarization = true,
            MaxSummaryLength = 200
        };

        var ratio = _compressor.EstimateCompressionRatio(dialogue, options);

        // 6轮对话，保留2轮，应该压缩4轮
        ratio.Should().BeLessThan(1.0);
    }

    [Fact]
    public void EstimateCompressionRatio_ShortDialogue_ShouldReturnOne()
    {
        var dialogue = @"User: Hello
Assistant: Hi!";

        var ratio = _compressor.EstimateCompressionRatio(dialogue, CompressionOptions.ForDialogue);

        ratio.Should().Be(1.0);
    }

    [Fact]
    public void EstimateCompressionRatio_EmptyContent_ShouldReturnOne()
    {
        var ratio = _compressor.EstimateCompressionRatio("", CompressionOptions.Default);
        ratio.Should().Be(1.0);
    }

    [Fact]
    public async Task CompressAsync_WithoutSummarization_ShouldNotIncludeSummary()
    {
        var dialogue = @"User: Question 1
Assistant: Answer 1
User: Question 2
Assistant: Answer 2";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 1,
            UseSummarization = false,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().NotContain("对话摘要");
    }

    [Fact]
    public async Task CompressAsync_WithoutPreservingKeyDecisions_ShouldNotIncludeDecisions()
    {
        var dialogue = @"User: Question
Assistant: Answer. 决定：使用方案A。";

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 1,
            PreserveKeyDecisions = false,
            MinCompressionThreshold = 10
        };
        var result = await _compressor.CompressAsync(dialogue, options).ConfigureAwait(true);

        result.Should().NotContain("关键决策");
    }

    [Fact]
    public async Task CompressAsync_CancellationRequested_ShouldThrowOperationCanceledException()
    {
        var dialogue = @"User: Question 1
Assistant: Answer 1
User: Question 2
Assistant: Answer 2
User: Question 3
Assistant: Answer 3
User: Question 4
Assistant: Answer 4";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new CompressionOptions
        {
            DialogueRoundsToPreserve = 2,
            MinCompressionThreshold = 10
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _compressor.CompressAsync(dialogue, options, cts.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
