namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// BridgeOAuthRetry 确定性测试 — IsAccessTokenValid token 有效性 + ShouldRetryAfterRefresh 刷新后重试决策纯函数。
/// 不发起真实 HTTP 请求，仅测纯计算分支。
/// </summary>
public class BridgeOAuthRetryTest {
    // === IsAccessTokenValid: token 非空非 null ===

    /// <summary>非空字符串返回 true。</summary>
    [Fact]
    public void IsAccessTokenValid_NonEmptyString_ReturnsTrue() {
        BridgeOAuthRetry.IsAccessTokenValid("token-abc").Should().BeTrue();
        BridgeOAuthRetry.IsAccessTokenValid("x").Should().BeTrue();
    }

    /// <summary>null 返回 false。</summary>
    [Fact]
    public void IsAccessTokenValid_Null_ReturnsFalse() {
        BridgeOAuthRetry.IsAccessTokenValid(null).Should().BeFalse();
    }

    /// <summary>空字符串返回 false。</summary>
    [Fact]
    public void IsAccessTokenValid_EmptyString_ReturnsFalse() {
        BridgeOAuthRetry.IsAccessTokenValid(string.Empty).Should().BeFalse();
    }

    /// <summary>空白字符串返回 true（实现仅检查 null/空，不检查空白）。</summary>
    [Fact]
    public void IsAccessTokenValid_Whitespace_ReturnsTrue() {
        // 实现为 !string.IsNullOrEmpty，空白字符串视为有效
        BridgeOAuthRetry.IsAccessTokenValid("   ").Should().BeTrue();
        BridgeOAuthRetry.IsAccessTokenValid("\t").Should().BeTrue();
    }

    // === ShouldRetryAfterRefresh: refreshed && newToken 有效 ===

    /// <summary>刷新成功且新 token 有效时返回 true。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_RefreshedAndValidToken_ReturnsTrue() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(true, "new-token").Should().BeTrue();
    }

    /// <summary>刷新失败时返回 false（即使新 token 有效）。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_NotRefreshed_ReturnsFalse() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(false, "new-token").Should().BeFalse();
    }

    /// <summary>刷新成功但新 token 为 null 时返回 false。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_RefreshedButNullToken_ReturnsFalse() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(true, null).Should().BeFalse();
    }

    /// <summary>刷新成功但新 token 为空字符串时返回 false。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_RefreshedButEmptyToken_ReturnsFalse() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(true, string.Empty).Should().BeFalse();
    }

    /// <summary>刷新失败且新 token 无效时返回 false（双重失败）。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_BothFail_ReturnsFalse() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(false, null).Should().BeFalse();
        BridgeOAuthRetry.ShouldRetryAfterRefresh(false, string.Empty).Should().BeFalse();
    }

    /// <summary>空白 token 视为有效（与 IsAccessTokenValid 一致）。</summary>
    [Fact]
    public void ShouldRetryAfterRefresh_RefreshedAndWhitespaceToken_ReturnsTrue() {
        BridgeOAuthRetry.ShouldRetryAfterRefresh(true, "  ").Should().BeTrue();
    }
}
