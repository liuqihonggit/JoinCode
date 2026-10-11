
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Bridge.Tests;

/// <summary>
/// BridgeJwtService 单元测试
/// 测试 HMAC-SHA256 JWT Token 生成、验证、刷新、过期检测和 Claims 解析
/// </summary>
public sealed class BridgeJwtServiceTests {
    private const string TestSecretKey = "test-secret-key-for-bridge-jwt-at-least-32-chars";

    private static BridgeJwtService CreateSut(string? secretKey = null, FakeTimeProvider? timeProvider = null) =>
        new(new BridgeConfig { JwtSecretKey = secretKey ?? TestSecretKey }, NullLogger.Instance, timeProvider);

    [Fact]
    public async Task GenerateToken_ShouldReturnValidToken_WhenValidClientId() {
        // Arrange
        await using var sut = CreateSut();

        // Act
        var token = sut.GenerateToken("client-001");

        // Assert
        token.Should().NotBeNullOrEmpty();
        var parts = token.Split('.');
        parts.Should().HaveCount(3, "JWT 应包含 header.payload.signature 三段");
    }

    [Fact]
    public async Task GenerateToken_ShouldThrow_WhenClientIdIsEmpty() {
        // Arrange
        await using var sut = CreateSut();

        // Act
        var act = () => sut.GenerateToken("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ValidateToken_ShouldReturnValid_WhenTokenIsValid() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-002");

        // Act
        var result = sut.ValidateToken(token);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Payload.Should().NotBeNull();
        result.Payload!.Sub.Should().Be("client-002");
        result.Payload.Iss.Should().Be("JccBridge");
    }

    [Fact]
    public async Task ValidateToken_ShouldReturnInvalid_WhenTokenIsTampered() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-003");
        // 篡改 payload 段
        var parts = token.Split('.');
        var tamperedToken = $"{parts[0]}.{parts[1][..^3]}XXX.{parts[2]}";

        // Act
        var result = sut.ValidateToken(tamperedToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Invalid signature");
    }

    [Fact]
    public async Task ValidateToken_ShouldReturnInvalid_WhenTokenIsExpired() {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        await using var sut = CreateSut(timeProvider: fakeTime);
        // 生成一个已过期的 token（1 秒有效期）
        var token = sut.GenerateToken("client-004", expirationSeconds: 1);

        // 推进时间使 token 过期
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        // Act
        var result = sut.ValidateToken(token);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Token expired");
        result.Payload.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateToken_ShouldReturnInvalid_WhenTokenFormatIsWrong() {
        // Arrange
        await using var sut = CreateSut();
        var malformedToken = "not.a.valid.jwt.token.format";

        // Act
        var result = sut.ValidateToken(malformedToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Invalid token format: expected 3 segments");
    }

    [Fact]
    public async Task RefreshToken_ShouldReturnNewToken_WhenTokenIsInRefreshWindow() {
        // Arrange
        await using var sut = CreateSut();
        // 生成一个有效期 299 秒的 token，使其立即进入刷新窗口（剩余 <= 300 秒）
        var token = sut.GenerateToken("client-005", expirationSeconds: 299);

        // Act
        var result = sut.RefreshToken(token);

        // Assert
        result.Success.Should().BeTrue();
        result.NewToken.Should().NotBe(token, "应签发新 Token");
        result.Error.Should().BeNull();

        // 新 Token 应可验证
        var validation = sut.ValidateToken(result.NewToken!);
        validation.IsValid.Should().BeTrue();
        validation.Payload!.Sub.Should().Be("client-005");
    }

    [Fact]
    public async Task RefreshToken_ShouldReturnSameToken_WhenTokenIsNotInRefreshWindow() {
        // Arrange
        await using var sut = CreateSut();
        // 默认 3600 秒有效期，远超 300 秒刷新窗口
        var token = sut.GenerateToken("client-006");

        // Act
        var result = sut.RefreshToken(token);

        // Assert
        result.Success.Should().BeTrue();
        result.NewToken.Should().Be(token, "未进入刷新窗口应返回原 Token");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task IsTokenExpired_ShouldReturnTrue_WhenTokenIsExpired() {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        await using var sut = CreateSut(timeProvider: fakeTime);
        var token = sut.GenerateToken("client-007", expirationSeconds: 1);

        // 推进时间使 token 过期
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        // Act
        var result = sut.IsTokenExpired(token);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTokenExpired_ShouldReturnFalse_WhenTokenIsNotExpired() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-008");

        // Act
        var result = sut.IsTokenExpired(token);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetClaims_ShouldReturnPayload_WhenTokenIsValid() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-009");

        // Act
        var claims = sut.GetClaims(token);

        // Assert
        claims.Should().NotBeNull();
        claims!.Sub.Should().Be("client-009");
        claims.Iss.Should().Be("JccBridge");
        claims.Iat.Should().BeGreaterThan(0);
        claims.Exp.Should().BeGreaterThan(claims.Iat);
    }

    [Fact]
    public async Task GetClaims_ShouldReturnNull_WhenTokenIsInvalid() {
        // Arrange
        await using var sut = CreateSut();
        var invalidToken = "invalid.token.value";

        // Act
        var claims = sut.GetClaims(invalidToken);

        // Assert
        claims.Should().BeNull();
    }

    [Fact]
    public async Task RevokeToken_ShouldInvalidateToken() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-revoke-001");

        // 验证 Token 初始有效
        sut.ValidateToken(token).IsValid.Should().BeTrue();

        // Act
        sut.RevokeToken(token);

        // Assert
        var result = sut.ValidateToken(token);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Token has been revoked");
    }

    [Fact]
    public async Task IsTokenRevoked_ShouldReturnTrue_WhenTokenIsRevoked() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-revoke-002");

        // Act
        sut.RevokeToken(token);

        // Assert
        sut.IsTokenRevoked(token).Should().BeTrue();
    }

    [Fact]
    public async Task IsTokenRevoked_ShouldReturnFalse_WhenTokenIsNotRevoked() {
        // Arrange
        await using var sut = CreateSut();
        var token = sut.GenerateToken("client-revoke-003");

        // Assert
        sut.IsTokenRevoked(token).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeToken_ShouldNotAffectOtherTokens() {
        // Arrange
        await using var sut = CreateSut();
        var token1 = sut.GenerateToken("client-revoke-004a");
        var token2 = sut.GenerateToken("client-revoke-004b");

        // Act
        sut.RevokeToken(token1);

        // Assert
        sut.ValidateToken(token1).IsValid.Should().BeFalse();
        sut.ValidateToken(token2).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpiredRevocations_ShouldRemoveExpiredRevokedTokens() {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        await using var sut = CreateSut(timeProvider: fakeTime);
        var token = sut.GenerateToken("client-cleanup-001", expirationSeconds: 1);
        sut.RevokeToken(token);

        // 推进时间使 Token 过期
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        // Act
        var cleaned = sut.CleanupExpiredRevocations();

        // Assert
        cleaned.Should().Be(1);
        sut.IsTokenRevoked(token).Should().BeFalse();
    }
}