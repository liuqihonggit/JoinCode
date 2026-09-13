
namespace Bridge.Tests;

/// <summary>
/// BridgeUIService 单元测试
/// 测试 QR 码数据生成、终端格式化、会话注册/注销
/// </summary>
public sealed class BridgeUIServiceTests
{
    private static BridgeUIService CreateSut() => new(logger: NullLogger<BridgeUIService>.Instance);

    [Fact]
    public async Task GenerateQRDataAsync_ShouldReturnQRData_WithValidFields()
    {
        // Arrange
        var sut = CreateSut();
        var sessionId = "test-session-001";
        var endpoint = "ws://localhost:3456";

        // Act
        var result = await sut.GenerateQRDataAsync(sessionId, endpoint).ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
        result.SessionId.Should().Be(sessionId);
        result.Endpoint.Should().Be(endpoint);
        result.Token.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateQRDataAsync_ShouldSetExpiration_BasedOnTtl()
    {
        // Arrange
        var sut = CreateSut();
        var sessionId = "test-session-002";
        var endpoint = "ws://localhost:3456";
        var ttlMs = 60000;
        var beforeCall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        var result = await sut.GenerateQRDataAsync(sessionId, endpoint, ttlMs).ConfigureAwait(true);
        var afterCall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        // ExpiresAt 应在 [beforeCall + ttlMs, afterCall + ttlMs] 范围内
        result.ExpiresAt.Should().BeInRange(beforeCall + ttlMs, afterCall + ttlMs);
    }

    [Fact]
    public async Task FormatAsTerminalQR_ShouldReturnNonEmptyString()
    {
        // Arrange
        var sut = CreateSut();
        var qrData = await sut.GenerateQRDataAsync("session-003", "ws://localhost:3456").ConfigureAwait(true);

        // Act
        var result = sut.FormatAsTerminalQR(qrData);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Bridge QR Code");
        result.Should().Contain("session-003");
    }

    [Fact]
    public async Task RegisterSession_ShouldAppearInActiveSessionList()
    {
        // Arrange
        var sut = CreateSut();
        var session = new BridgeSessionDisplay
        {
            SessionId = "active-session-001",
            ClientName = "TestClient",
            Status = "connected",
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        sut.RegisterSession(session);
        var activeSessions = await sut.GetActiveSessionList().ConfigureAwait(true);

        // Assert
        activeSessions.Should().HaveCount(1);
        activeSessions[0].SessionId.Should().Be("active-session-001");
        activeSessions[0].ClientName.Should().Be("TestClient");
    }

    [Fact]
    public async Task UnregisterSession_ShouldRemoveFromActiveSessionList()
    {
        // Arrange
        var sut = CreateSut();
        var session = new BridgeSessionDisplay
        {
            SessionId = "remove-session-001",
            ClientName = "ToRemove",
            Status = "connected",
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        sut.RegisterSession(session);

        // Act
        sut.UnregisterSession("remove-session-001");
        var activeSessions = await sut.GetActiveSessionList().ConfigureAwait(true);

        // Assert
        activeSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveSessionList_ShouldReturnEmpty_WhenNoSessions()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var activeSessions = await sut.GetActiveSessionList().ConfigureAwait(true);

        // Assert
        activeSessions.Should().BeEmpty();
    }

    /// <summary>
    /// 防止循环依赖回归 — BridgeUIService 构造函数不得依赖 BridgeServer
    /// 根因: BridgeServer → BridgeServerSession → BridgeUIService → BridgeServer 形成循环
    /// 修复: 移除 BridgeUIService 对 BridgeServer 的无效依赖（字段从未被使用）
    /// </summary>
    [Fact]
    public void Constructor_ShouldNotDependOnBridgeServer_ToPreventCircularDependency()
    {
        // Act
        var constructors = typeof(BridgeUIService).GetConstructors();
        var parameterTypes = constructors
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToArray();

        // Assert
        parameterTypes
            .Should().NotContain(typeof(BridgeServer),
                "BridgeUIService 依赖 BridgeServer 会导致 DI 循环依赖: " +
                "BridgeServer → BridgeServerSession → BridgeUIService → BridgeServer");
    }
}
