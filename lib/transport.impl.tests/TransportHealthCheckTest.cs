namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// TcpPortHealthCheck 构造函数守卫确定性测试 — host null/空 + port 范围 [0,65535]
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class TransportHealthCheckTest {
    [Fact]
    public void Ctor_NullHost_ThrowsArgumentNullException() {
        var act = () => new TcpPortHealthCheck(null!, 80);
        act.Should().Throw<ArgumentNullException>().WithParameterName("host");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_EmptyOrWhitespaceHost_ThrowsArgumentException(string host) {
        var act = () => new TcpPortHealthCheck(host, 80);
        act.Should().Throw<ArgumentException>().WithParameterName("host");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-65536)]
    public void Ctor_NegativePort_ThrowsArgumentOutOfRangeException(int port) {
        var act = () => new TcpPortHealthCheck("localhost", port);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("port");
    }

    [Fact]
    public void Ctor_PortAbove65535_ThrowsArgumentOutOfRangeException() {
        var act = () => new TcpPortHealthCheck("localhost", 65536);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("port");
    }

    [Fact]
    public void Ctor_PortZero_DoesNotThrow() {
        var check = new TcpPortHealthCheck("localhost", 0);
        check.TransportType.Should().Be("tcp");
    }

    [Fact]
    public void Ctor_Port65535_DoesNotThrow() {
        var check = new TcpPortHealthCheck("localhost", 65535);
        check.TransportType.Should().Be("tcp");
    }
}
