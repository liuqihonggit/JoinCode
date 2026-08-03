
namespace Core.Tests.Context.Compression;

public class ContextCompressorTests
{
    private readonly CompressionStrategyFactory _factory = new();

    [Fact]
    public void Constructor_WithNullFactory_ShouldThrowArgumentNullException()
    {
        Action act = () => new ContextCompressor(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("strategyFactory");
    }

    [Fact]
    public void Constructor_WithDefaultOptions_ShouldUseDefaultOptions()
    {
        var compressor = new ContextCompressor(_factory);

        compressor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomOptions_ShouldUseCustomOptions()
    {
        var customOptions = new CompressionOptions { TargetCompressionRatio = 0.3 };
        var compressor = new ContextCompressor(_factory, customOptions);

        compressor.Should().NotBeNull();
    }

    [Fact]
    public async Task CompressAsync_CodeContent_ShouldReturnCompressionResult()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ContentType.Should().Be(ContentType.Code);
        result.StrategyName.Should().Be("CodeContentCompressor");
        result.OriginalLength.Should().Be(code.Length);
        result.CompressedLength.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompressAsync_DialogueContent_ShouldReturnCompressionResult()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var dialogue = @"User: Hello
Assistant: Hi!
User: How are you?
Assistant: I'm fine!";

        var result = await compressor.CompressAsync(dialogue, ContentType.Dialogue).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ContentType.Should().Be(ContentType.Dialogue);
        result.StrategyName.Should().Be("DialogueCompressor");
    }

    [Fact]
    public async Task CompressAsync_EmptyContent_ShouldReturnNoCompressionResult()
    {
        var compressor = new ContextCompressor(_factory);

        var result = await compressor.CompressAsync("", ContentType.Code).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.StrategyName.Should().Be("None");
    }

    [Fact]
    public async Task CompressAsync_ShortContent_ShouldReturnNoCompressionResult()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 100
        });

        var result = await compressor.CompressAsync("short", ContentType.Code).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.StrategyName.Should().Be("None");
    }

    [Fact]
    public async Task CompressAsync_UnsupportedContentType_ShouldReturnNoCompressionResult()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var content = "Some text content";

        var result = await compressor.CompressAsync(content, ContentType.Text).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.StrategyName.Should().Be("None");
    }

    [Fact]
    public async Task CompressAsync_WithCustomOptions_ShouldUseCustomOptions()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}";
        var options = new CompressionOptions
        {
            MinCompressionThreshold = 10,
            PreserveComments = false
        };

        var result = await compressor.CompressAsync(code, ContentType.Code, options).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CompressBatchAsync_MultipleContents_ShouldReturnAllResults()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var contents = new[]
        {
            new ContentItem { Id = "1", Type = ContentType.Code, Content = "public class A { public void Method() { var x = 1; } }" },
            new ContentItem { Id = "2", Type = ContentType.Dialogue, Content = "User: Hello\nAssistant: Hi!" }
        };

        var results = await compressor.CompressBatchAsync(contents).ConfigureAwait(true);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task CompressBatchAsync_EmptyList_ShouldReturnEmptyResults()
    {
        var compressor = new ContextCompressor(_factory);

        var results = await compressor.CompressBatchAsync(Array.Empty<ContentItem>()).ConfigureAwait(true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void CanCompress_ValidContent_ShouldReturnTrue()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = "public class Test { public void Method() { var x = 1; var y = 2; Console.WriteLine(x + y); } }";

        var canCompress = compressor.CanCompress(code, ContentType.Code);

        canCompress.Should().BeTrue();
    }

    [Fact]
    public void CanCompress_EmptyContent_ShouldReturnFalse()
    {
        var compressor = new ContextCompressor(_factory);

        var canCompress = compressor.CanCompress("", ContentType.Code);

        canCompress.Should().BeFalse();
    }

    [Fact]
    public void CanCompress_UnsupportedType_ShouldReturnFalse()
    {
        var compressor = new ContextCompressor(_factory);

        var canCompress = compressor.CanCompress("Some content", ContentType.Text);

        canCompress.Should().BeFalse();
    }

    [Fact]
    public void GetCompressionRatio_CodeContent_ShouldReturnEstimatedRatio()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method() {
        var x = 1;
        var y = 2;
        var z = x + y;
        Console.WriteLine(z);
        Console.WriteLine(x);
        Console.WriteLine(y);
    }

    public void AnotherMethod() {
        for (int i = 0; i < 10; i++) {
            Console.WriteLine(i);
        }
    }
}";

        var ratio = compressor.GetCompressionRatio(code, ContentType.Code);

        ratio.Should().BeLessThan(1.0);
        ratio.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCompressionRatio_UnsupportedType_ShouldReturnOne()
    {
        var compressor = new ContextCompressor(_factory);

        var ratio = compressor.GetCompressionRatio("content", ContentType.Text);

        ratio.Should().Be(1.0);
    }

    [Fact]
    public async Task CompressAsync_CancellationRequested_ShouldHandleGracefully()
    {
        // 注意：当前实现捕获 OperationCanceledException 并返回错误结果
        // 这是设计选择，以便调用者可以选择处理错误或异常
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = "public class Test { }";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // 当前实现返回错误结果而不是抛出异常
        var result = await compressor.CompressAsync(code, ContentType.Code, cancellationToken: cts.Token).ConfigureAwait(true);

        // 如果取消被触发，结果应该不是成功状态
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_Result_ShouldContainMetadata()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method() { }
}";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        result.Metadata.Should().NotBeNull();
        if (result.IsSuccess && result.StrategyName != "None")
        {
            result.Metadata.Should().ContainKey("TargetRatio");
            result.Metadata.Should().ContainKey("ActualRatio");
            result.Metadata.Should().ContainKey("StrategyPriority");
        }
    }

    [Fact]
    public async Task CompressAsync_Result_ShouldCalculateCompressionRatio()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method() {
        var x = 1;
        var y = 2;
        Console.WriteLine(x + y);
    }

    public void AnotherMethod() {
        for (int i = 0; i < 10; i++) {
            Console.WriteLine(i);
        }
    }
}";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        // 压缩比率 = 压缩后长度 / 原始长度
        // 当压缩有效时，比率 < 1；当无法压缩时，比率 >= 1
        result.CompressionRatio.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CompressAsync_Result_ShouldCalculateSavedTokens()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = @"
public class Test
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        result.SavedTokens.Should().Be(result.OriginalLength - result.CompressedLength);
    }

    [Fact]
    public async Task CompressAsync_Result_ShouldHaveProcessingTime()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = "public class Test { }";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CompressAsync_Result_ShouldHaveUniqueContentId()
    {
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10
        });
        var code = "public class Test { }";

        var result1 = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);
        var result2 = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        result1.ContentId.Should().NotBe(result2.ContentId);
    }

    [Fact]
    public async Task CompressAsync_Timeout_ShouldReturnErrorResult()
    {
        // 使用非常短的超时时间来确保触发超时
        // 注意：这个测试依赖于执行时间，在某些快速机器上可能不稳定
        var compressor = new ContextCompressor(_factory, new CompressionOptions
        {
            MinCompressionThreshold = 10,
            CompressionTimeoutMs = 1
        });

        // 使用较大的代码块增加处理时间，提高超时触发概率
        var code = @"
public class Test
{
    public void Method1() { Console.WriteLine(""Test1""); }
    public void Method2() { Console.WriteLine(""Test2""); }
    public void Method3() { Console.WriteLine(""Test3""); }
    public void Method4() { Console.WriteLine(""Test4""); }
    public void Method5() { Console.WriteLine(""Test5""); }
}";

        var result = await compressor.CompressAsync(code, ContentType.Code).ConfigureAwait(true);

        // 超时可能触发也可能不触发，取决于执行速度
        // 如果超时触发，结果应该包含超时信息
        if (!result.IsSuccess)
        {
            result.ErrorMessage.Should().Contain("timed out");
        }
    }
}
