namespace Abs.Tests.Security;

/// <summary>
/// PreapprovedDomains 确定性单元测试 — 覆盖 CreateHostSet(internal) + Hosts(public) + IsPreapprovedHost/Url(public)。
/// 纯集合/字符串匹配,不依赖时序/IO。
/// </summary>
public sealed class PreapprovedDomainsTests {

    // ── CreateHostSet: 集合构造 ──

    [Fact]
    public void CreateHostSet_ReturnsNonEmptySet() {
        var set = PreapprovedDomains.CreateHostSet();
        set.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateHostSet_MatchesPublicHostsField() {
        // CreateHostSet 应与公开 Hosts 字段内容一致
        var set = PreapprovedDomains.CreateHostSet();
        set.Should().Equal(PreapprovedDomains.Hosts);
    }

    [Fact]
    public void CreateHostSet_CaseInsensitive() {
        var set = PreapprovedDomains.CreateHostSet();
        set.Contains("DOCS.PYTHON.ORG").Should().BeTrue();
        set.Contains("docs.PYTHON.org").Should().BeTrue();
    }

    // ── Hosts: 公开集合 ──

    [Theory]
    [InlineData("docs.python.org")]
    [InlineData("learn.microsoft.com")]
    [InlineData("developer.mozilla.org")]
    [InlineData("nodejs.org")]
    [InlineData("stackoverflow.com")]
    [InlineData("docs.github.com")]
    [InlineData("raw.githubusercontent.com")]
    [InlineData("kubernetes.io")]
    [InlineData("www.docker.com")]
    public void Hosts_ContainsKnownDomains(string host) {
        PreapprovedDomains.Hosts.Contains(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("malware.org")]
    [InlineData("attacker.io")]
    public void Hosts_DoesNotContainUnknownDomains(string host) {
        PreapprovedDomains.Hosts.Contains(host).Should().BeFalse();
    }

    // ── IsPreapprovedHost: 主机名匹配 ──

    [Fact]
    public void IsPreapprovedHost_KnownHost_ReturnsTrue() {
        PreapprovedDomains.IsPreapprovedHost("docs.python.org").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedHost_CaseInsensitive_ReturnsTrue() {
        PreapprovedDomains.IsPreapprovedHost("DOCS.PYTHON.ORG").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedHost_UnknownHost_ReturnsFalse() {
        PreapprovedDomains.IsPreapprovedHost("evil.com").Should().BeFalse();
    }

    [Fact]
    public void IsPreapprovedHost_StripsWwwPrefix() {
        // www. 前缀剥离后查 Hosts
        // stackoverflow.com 在 Hosts → www.stackoverflow.com 也应通过(剥 www 后查 stackoverflow.com)
        PreapprovedDomains.IsPreapprovedHost("www.stackoverflow.com").Should().BeTrue();
    }

    // ── IsPreapprovedUrl: URL 匹配(主机名 + 路径前缀) ──

    [Fact]
    public void IsPreapprovedUrl_KnownHostAnyPath_ReturnsTrue() {
        PreapprovedDomains.IsPreapprovedUrl("https://docs.python.org/3/library/").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedUrl_UnknownHost_ReturnsFalse() {
        PreapprovedDomains.IsPreapprovedUrl("https://evil.com/path").Should().BeFalse();
    }

    [Fact]
    public void IsPreapprovedUrl_InvalidUrl_ReturnsFalse() {
        PreapprovedDomains.IsPreapprovedUrl("not a url").Should().BeFalse();
    }

    [Fact]
    public void IsPreapprovedUrl_GithubAnthropicsPrefix_ReturnsTrue() {
        PreapprovedDomains.IsPreapprovedUrl("https://github.com/anthropics/some-repo").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedUrl_GithubOtherPrefix_ReturnsFalse() {
        PreapprovedDomains.IsPreapprovedUrl("https://github.com/other/repo").Should().BeFalse();
    }

    [Fact]
    public void IsPreapprovedUrl_GithubAnthropicsEvilPrefix_ReturnsFalse() {
        // 强制路径段边界: /anthropics 不匹配 /anthropics-evil
        PreapprovedDomains.IsPreapprovedUrl("https://github.com/anthropics-evil/malware").Should().BeFalse();
    }

    [Fact]
    public void IsPreapprovedUrl_GithubAnthropicsExact_ReturnsTrue() {
        // 路径恰好等于前缀
        PreapprovedDomains.IsPreapprovedUrl("https://github.com/anthropics").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedUrl_VercelDocsPrefix_ReturnsTrue() {
        PreapprovedDomains.IsPreapprovedUrl("https://vercel.com/docs/guide").Should().BeTrue();
    }

    [Fact]
    public void IsPreapprovedUrl_VercelOtherPrefix_ReturnsFalse() {
        PreapprovedDomains.IsPreapprovedUrl("https://vercel.com/blog").Should().BeFalse();
    }
}
