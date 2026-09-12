namespace PrefixCache.Unit;

public sealed class CacheTtlResolverTests
{
    [Fact]
    public void DashScope_Host_Returns5Minutes()
    {
        CacheTtlResolver.DefaultCacheTtl("https://dashscope.aliyuncs.com/api/v1").Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void DashScope_SuffixHost_Returns5Minutes()
    {
        CacheTtlResolver.DefaultCacheTtl("https://proxy.dashscope.aliyuncs.com").Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void MaasSuffixHost_Returns5Minutes()
    {
        CacheTtlResolver.DefaultCacheTtl("https://qwen.maas.aliyuncs.com").Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Anthropic_Host_Returns5Minutes()
    {
        CacheTtlResolver.DefaultCacheTtl("https://api.anthropic.com").Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Anthropic_SuffixHost_Returns5Minutes()
    {
        CacheTtlResolver.DefaultCacheTtl("https://proxy.anthropic.com").Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void DeepSeek_Host_Returns24Hours()
    {
        CacheTtlResolver.DefaultCacheTtl("https://api.deepseek.com").Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void DeepSeek_SuffixHost_Returns24Hours()
    {
        CacheTtlResolver.DefaultCacheTtl("https://proxy.deepseek.com").Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void UnknownHost_Returns24HoursConservative()
    {
        CacheTtlResolver.DefaultCacheTtl("https://proxy.example.com/api").Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void NullOrEmpty_Returns24HoursConservative()
    {
        CacheTtlResolver.DefaultCacheTtl(null).Should().Be(TimeSpan.FromHours(24));
        CacheTtlResolver.DefaultCacheTtl(string.Empty).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void MalformedUrl_Returns24HoursConservative()
    {
        CacheTtlResolver.DefaultCacheTtl("not a url").Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void UnrelatedHostWithDeepseekSubstring_DoesNotMisdetect()
    {
        CacheTtlResolver.DefaultCacheTtl("https://notdeepseek.example.com").Should().Be(TimeSpan.FromHours(24));
    }
}