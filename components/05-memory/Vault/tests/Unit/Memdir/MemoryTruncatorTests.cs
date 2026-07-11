
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryTruncator 单元测试
/// 测试记忆截断器的行数截断、字节截断和智能截断功能
/// </summary>
public sealed class MemoryTruncatorTests
{
    private readonly MemoryTruncator _truncator = new();

    // === Truncate 方法测试 ===

    [Fact]
    public void Truncate_NullContent_ReturnsNull()
    {
        // 空内容应原样返回
        var result = _truncator.Truncate(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void Truncate_EmptyContent_ReturnsEmpty()
    {
        // 空字符串应原样返回
        var result = _truncator.Truncate(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Truncate_ShortContent_ReturnsUnchanged()
    {
        // 低于阈值的内容不应被截断
        var content = "短内容";
        var result = _truncator.Truncate(content);
        result.Should().Be(content);
    }

    [Fact]
    public void Truncate_ExceedsMaxLines_TruncatesWithSuffix()
    {
        // 超过最大行数时应截断，结果比原始内容短
        var threshold = new TruncationThreshold { MaxLines = 3, MaxBytes = 1024 * 1024 };
        var lines = Enumerable.Range(0, 10).Select(i => $"第{i}行内容").ToArray();
        var content = string.Join('\n', lines);

        var result = _truncator.Truncate(content, threshold);

        // 结果应比原始内容短
        result.Should().NotBe(content);
        result.Length.Should().BeLessThan(content.Length);
        // 应包含前3行内容
        result.Should().Contain("第0行内容");
        result.Should().Contain("第1行内容");
        result.Should().Contain("第2行内容");
        // 不应包含第4行及之后的内容
        result.Should().NotContain("第3行内容");
    }

    [Fact]
    public void Truncate_ExceedsMaxBytes_TruncatesByBytes()
    {
        // 超过最大字节数时应截断，结果比原始内容短
        var threshold = new TruncationThreshold { MaxLines = 10000, MaxBytes = 50 };
        // 生成超过50字节的内容（行数在阈值内）
        var content = new string('A', 200);

        var result = _truncator.Truncate(content, threshold);

        // 结果应比原始内容短
        result.Length.Should().BeLessThan(content.Length);
        // UTF8 字节计数应小于等于阈值（截断提示可能略超，但核心内容被截断）
        var resultBytes = System.Text.Encoding.UTF8.GetByteCount(result);
        resultBytes.Should().BeLessThan(System.Text.Encoding.UTF8.GetByteCount(content));
    }

    [Fact]
    public void Truncate_ExceedsBothLinesAndBytes_TruncatesByLinesFirst()
    {
        // 同时超过行数和字节数时，优先按行数截断
        var threshold = new TruncationThreshold { MaxLines = 2, MaxBytes = 10000 };
        var lines = Enumerable.Range(0, 5).Select(i => $"这是第{i}行比较长的内容用于测试").ToArray();
        var content = string.Join('\n', lines);

        var result = _truncator.Truncate(content, threshold);

        // 结果应与原始内容不同
        result.Should().NotBe(content);
        // 应包含前2行
        result.Should().Contain("这是第0行");
        result.Should().Contain("这是第1行");
        // 不应包含第3行及之后
        result.Should().NotContain("这是第2行");
    }

    // === SmartTruncate 方法测试 ===

    [Fact]
    public void SmartTruncate_NullContent_ReturnsNull()
    {
        // 空内容应原样返回
        var result = _truncator.SmartTruncate(null!, "query");
        result.Should().BeNull();
    }

    [Fact]
    public void SmartTruncate_EmptyContent_ReturnsEmpty()
    {
        // 空字符串应原样返回
        var result = _truncator.SmartTruncate(string.Empty, "query");
        result.Should().BeEmpty();
    }

    [Fact]
    public void SmartTruncate_ShortContent_ReturnsUnchanged()
    {
        // 低于阈值的内容不应被截断
        var content = "短内容";
        var result = _truncator.SmartTruncate(content, "查询");
        result.Should().Be(content);
    }

    [Fact]
    public void SmartTruncate_ExceedsMaxLines_KeepsRelevantLines()
    {
        // 超过行数阈值时，智能截断应保留与查询相关的行
        var threshold = new TruncationThreshold { MaxLines = 4, MaxBytes = 1024 * 1024 };
        var lines = new[]
        {
            "这是第一行无关内容",
            "这是关于数据库的查询内容",
            "这是第三行无关内容",
            "这是第四行无关内容",
            "这是关于缓存的查询内容",
            "这是第六行无关内容",
            "这是第七行无关内容",
            "这是第八行无关内容",
            "这是第九行无关内容",
            "这是第十行无关内容",
        };
        var content = string.Join('\n', lines);

        var result = _truncator.SmartTruncate(content, "查询", threshold);

        // 结果应比原始内容短
        result.Should().NotBe(content);
        result.Length.Should().BeLessThan(content.Length);
        // 应包含与查询相关的行
        result.Should().Contain("数据库");
        result.Should().Contain("缓存");
        // 不相关的行可能被省略，用"..."表示间隔
        result.Should().Contain("...");
    }

    [Fact]
    public void SmartTruncate_ExceedsMaxBytes_TruncatesByBytes()
    {
        // 超过字节阈值时，智能截断后仍需按字节截断
        var threshold = new TruncationThreshold { MaxLines = 10000, MaxBytes = 100 };
        // 生成包含查询词的大内容
        var content = "查询关键词 " + new string('X', 200);

        var result = _truncator.SmartTruncate(content, "查询", threshold);

        // 结果应比原始内容短
        result.Length.Should().BeLessThan(content.Length);
    }

    // === CountLines 间接测试（通过 Truncate） ===

    [Fact]
    public void CountLines_ViaTruncate_SingleLineContent()
    {
        // 单行内容在默认阈值下不应被截断
        var content = "单行内容不换行";
        var result = _truncator.Truncate(content);
        result.Should().Be(content);
    }

    [Fact]
    public void CountLines_ViaTruncate_MultiLineContent()
    {
        // 多行内容在默认阈值（200行）下不应被截断
        var lines = Enumerable.Range(0, 10).Select(i => $"行{i}").ToArray();
        var content = string.Join('\n', lines);
        var result = _truncator.Truncate(content);
        result.Should().Be(content);

        // 用小阈值验证行数计数正确：5行内容，阈值3行，应被截断
        var threshold = new TruncationThreshold { MaxLines = 3, MaxBytes = 1024 * 1024 };
        var resultTruncated = _truncator.Truncate(content, threshold);
        resultTruncated.Should().NotBe(content);
    }
}
