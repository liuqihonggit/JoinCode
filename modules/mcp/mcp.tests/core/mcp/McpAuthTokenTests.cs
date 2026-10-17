namespace Mcp.Tests;

/// <summary>
/// McpAuthToken 纯逻辑单元测试 — token 生成与校验（ADR 0138: mcp serve 默认 token 鉴权）
/// </summary>
public class McpAuthTokenTests {
    [Fact]
    public void Generate_Returns64HexChars() {
        var token = McpAuthToken.Generate();
        token.Should().NotBeNullOrEmpty();
        token.Should().MatchRegex("^[0-9a-fA-F]{64}$");
    }

    [Fact]
    public void Generate_TwoTokens_AreDifferent() {
        var a = McpAuthToken.Generate();
        var b = McpAuthToken.Generate();
        a.Should().NotBe(b);
    }

    [Fact]
    public void Validate_MissingAuthHeader_ReturnsFalse() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([], token, out _).Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingAuthHeader_ProvidesGuidance() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("Host", "localhost")], token, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty("错误提示必须带引导（AGENTS.md 规则9）");
        reason.Should().ContainAny("Mcp-Token", "Authorization");
    }

    [Fact]
    public void Validate_WrongToken_ReturnsFalse() {
        var token = McpAuthToken.Generate();
        var wrong = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("Authorization", $"Bearer {wrong}")], token, out _).Should().BeFalse();
    }

    [Fact]
    public void Validate_BearerCorrect_ReturnsTrue() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("Authorization", $"Bearer {token}")], token, out _).Should().BeTrue();
    }

    [Fact]
    public void Validate_McpTokenHeaderCorrect_ReturnsTrue() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("Mcp-Token", token)], token, out _).Should().BeTrue();
    }

    [Fact]
    public void Validate_HeaderCaseInsensitive_Matches() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("mcp-token", token)], token, out _).Should().BeTrue("TCP 头解析后大小写不定，校验必须不区分大小写");
        McpAuthToken.TryValidate([new("authorization", $"bearer {token}")], token, out _).Should().BeTrue();
    }

    [Fact]
    public void Validate_OriginHeaderAlone_IsNotSufficient() {
        var token = McpAuthToken.Generate();
        McpAuthToken.TryValidate([new("Origin", "https://example.com")], token, out _).Should().BeFalse("Origin 校验不能替代 token 鉴权");
    }

    [Fact]
    public void Constructor_EmptyToken_Throws() {
        var act = () => McpAuthToken.For("");
        act.Should().Throw<ArgumentException>().WithParameterName("token");
    }

    [Fact]
    public void Constructor_WhitespaceToken_Throws() {
        var act = () => McpAuthToken.For("   ");
        act.Should().Throw<ArgumentException>().WithParameterName("token");
    }

    [Fact]
    public void For_ExplicitToken_UsesGivenValue() {
        var token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        McpAuthToken.For(token).Value.Should().Be(token);
    }
}
