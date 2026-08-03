
namespace Core.Tests.Context.Compression;

public class ReferenceIndexCompressorTests
{
    private readonly ReferenceIndexCompressor _compressor = new();

    [Fact]
    public void Name_ShouldReturnCorrectValue()
    {
        _compressor.Name.Should().Be("ReferenceIndexCompressor");
    }

    [Fact]
    public void SupportedContentTypes_ShouldContainReferenceIndex()
    {
        _compressor.SupportedContentTypes.Should().Contain(ContentType.ReferenceIndex);
    }

    [Fact]
    public void CanHandle_ReferenceIndexContent_ShouldReturnTrue()
    {
        var content = @"文件: Test.cs
class Test
method Method1

文件: Another.cs
class Another
method Method2

文件: Third.cs
class Third";
        _compressor.CanHandle(content, ContentType.ReferenceIndex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonReferenceIndexContent_ShouldReturnFalse()
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
    public async Task CompressAsync_ShouldPreserveFilePaths()
    {
        var content = @"文件: Test.cs
class Test

文件: Another.cs
class Another";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        result.Should().Contain("Test.cs");
        result.Should().Contain("Another.cs");
    }

    [Fact]
    public async Task CompressAsync_ShouldGroupByFile()
    {
        var content = @"文件: Test.cs
class Test
method Method1
method Method2";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        result.Should().Contain("文件: Test.cs");
    }

    [Fact]
    public async Task CompressAsync_ShouldRespectMaxEntries()
    {
        var content = string.Join("\n", Enumerable.Range(0, 10).Select(i =>
            $"文件: Test{i}.cs\nclass Test{i}"));

        var options = new CompressionOptions { MaxReferenceEntries = 3 };
        var result = await _compressor.CompressAsync(content, options).ConfigureAwait(true);

        result.Should().Contain("总计: 10 个引用");
        result.Should().Contain("显示: 3 个重要引用");
    }

    [Fact]
    public async Task CompressAsync_ShouldShowRemainingCount()
    {
        var content = string.Join("\n", Enumerable.Range(0, 10).Select(i =>
            $"文件: Test{i}.cs\nclass Test{i}"));

        var options = new CompressionOptions { MaxReferenceEntries = 5 };
        var result = await _compressor.CompressAsync(content, options).ConfigureAwait(true);

        result.Should().Contain("还有 5 个引用未显示");
    }

    [Fact]
    public async Task CompressAsync_WithEnglishFormat_ShouldHandleCorrectly()
    {
        var content = @"File: Test.cs
class Test

File: Another.cs
class Another";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        result.Should().Contain("Test.cs");
        result.Should().Contain("Another.cs");
    }

    [Fact]
    public async Task CompressAsync_WithRawFilePaths_ShouldHandleCorrectly()
    {
        // 使用标准引用索引格式，包含文件路径前缀
        var content = @"文件: C:\Project\Test.cs
class Test

文件: C:\Project\Another.cs
class Another";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        // 压缩结果应该包含文件名
        result.Should().Contain("Test.cs");
        result.Should().Contain("Another.cs");
    }

    [Fact]
    public void EstimateCompressionRatio_ManyEntries_ShouldReturnLowerRatio()
    {
        var content = string.Join("\n", Enumerable.Range(0, 100).Select(i =>
            $"文件: Test{i}.cs\nclass Test{i}"));

        var options = new CompressionOptions { MaxReferenceEntries = 20 };
        var ratio = _compressor.EstimateCompressionRatio(content, options);

        ratio.Should().BeLessThan(1.0);
        ratio.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateCompressionRatio_FewEntries_ShouldReturnOne()
    {
        var content = @"文件: Test.cs
class Test";

        var options = new CompressionOptions { MaxReferenceEntries = 10 };
        var ratio = _compressor.EstimateCompressionRatio(content, options);

        ratio.Should().Be(1.0);
    }

    [Fact]
    public void EstimateCompressionRatio_EmptyContent_ShouldReturnOne()
    {
        var ratio = _compressor.EstimateCompressionRatio("", CompressionOptions.Default);
        ratio.Should().Be(1.0);
    }

    [Fact]
    public async Task CompressAsync_WithIdentifiers_ShouldIncludeInOutput()
    {
        var content = @"文件: Test.cs
class TestClass
method TestMethod";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        result.Should().Contain("Test.cs");
    }

    [Fact]
    public async Task CompressAsync_CancellationRequested_ShouldThrowOperationCanceledException()
    {
        var content = "文件: Test.cs\nclass Test";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _compressor.CompressAsync(content, CompressionOptions.Default, cts.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CompressAsync_WithReferences_ShouldCountReferences()
    {
        var content = @"文件: Test.cs
class Test
引用: Other1
引用: Other2";

        var result = await _compressor.CompressAsync(content, CompressionOptions.ForReferenceIndex).ConfigureAwait(true);

        result.Should().Contain("引用: 2");
    }
}
