namespace Core.Utils;

/// <summary>
/// TransportFrame 确定性单元测试 — 验证消息帧构造/属性/编解码,不依赖管道通信/时序。
/// <para>补充 E2E 测试缺少的确定性验证部分:TransportFrame 的构造、相等性、UTF-8 编解码往返、边界。</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class TransportFramePureFunctionsTest {

    /// <summary>构造 — SourceProcessId 和 Data 属性正确</summary>
    [Fact]
    public void Constructor_PropertiesCorrect() {
        var data = Encoding.UTF8.GetBytes("hello");
        var frame = new TransportFrame("pid-1", data);

        frame.SourceProcessId.Should().Be("pid-1");
        frame.Data.Span.ToArray().Should().Equal(data);
    }

    /// <summary>相同参数的 TransportFrame 相等(record 值相等语义)</summary>
    [Fact]
    public void SameArgs_AreEqual() {
        var data = Encoding.UTF8.GetBytes("msg");
        var f1 = new TransportFrame("pid", data);
        var f2 = new TransportFrame("pid", data);

        f1.Should().Be(f2);
        (f1 == f2).Should().BeTrue();
    }

    /// <summary>不同 SourceProcessId 的 TransportFrame 不相等</summary>
    [Fact]
    public void DifferentPid_NotEqual() {
        var data = Encoding.UTF8.GetBytes("msg");
        var f1 = new TransportFrame("pid-1", data);
        var f2 = new TransportFrame("pid-2", data);

        f1.Should().NotBe(f2);
    }

    /// <summary>UTF-8 编解码往返 — 编码后解码应得原字符串</summary>
    [Fact]
    public void Utf8RoundTrip_PreservesContent() {
        const string original = "np-e2e-msg-测试-🎯";
        var data = Encoding.UTF8.GetBytes(original);
        var frame = new TransportFrame("pid", data);

        var decoded = Encoding.UTF8.GetString(frame.Data.Span);
        decoded.Should().Be(original);
    }

    /// <summary>空 Data — 边界,不抛异常</summary>
    [Fact]
    public void EmptyData_NoThrow() {
        var frame = new TransportFrame("pid", ReadOnlyMemory<byte>.Empty);

        frame.Data.Length.Should().Be(0);
        Encoding.UTF8.GetString(frame.Data.Span).Should().BeEmpty();
    }

    /// <summary>空 SourceProcessId — 边界,不抛异常</summary>
    [Fact]
    public void EmptySourceProcessId_NoThrow() {
        var frame = new TransportFrame("", Encoding.UTF8.GetBytes("msg"));

        frame.SourceProcessId.Should().BeEmpty();
    }

    /// <summary>大 Data — 边界,1MB 数据正确传输</summary>
    [Fact]
    public void LargeData_PreservesContent() {
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);
        var frame = new TransportFrame("pid", data);

        frame.Data.Length.Should().Be(1024 * 1024);
        frame.Data.Span.ToArray().Should().Equal(data);
    }
}
