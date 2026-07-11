
namespace Bridge.Tests.Phase7B;

public sealed class SessionIdCompatTests
{
    [Fact]
    public void ToCompatSessionId_CsePrefix_ConvertsToSession()
    {
        // shim 默认启用（gate 为 null），cse_ → session_
        SessionIdCompat.SetCseShimGate(null);
        var result = SessionIdCompat.ToCompatSessionId("cse_abc123");
        Assert.Equal("session_abc123", result);
    }

    [Fact]
    public void ToCompatSessionId_CsePrefixShimDisabled_ReturnsAsIs()
    {
        // shim 禁用时，cse_ 不转换
        SessionIdCompat.SetCseShimGate(() => false);
        var result = SessionIdCompat.ToCompatSessionId("cse_abc123");
        Assert.Equal("cse_abc123", result);
        SessionIdCompat.SetCseShimGate(null);
    }

    [Fact]
    public void ToCompatSessionId_SessionPrefix_ReturnsAsIs()
    {
        var result = SessionIdCompat.ToCompatSessionId("session_abc123");
        Assert.Equal("session_abc123", result);
    }

    [Fact]
    public void ToCompatSessionId_NoPrefix_ReturnsAsIs()
    {
        var result = SessionIdCompat.ToCompatSessionId("plainid");
        Assert.Equal("plainid", result);
    }

    [Fact]
    public void ToInfraSessionId_SessionPrefix_ConvertsToCse()
    {
        var result = SessionIdCompat.ToInfraSessionId("session_abc123");
        Assert.Equal("cse_abc123", result);
    }

    [Fact]
    public void ToInfraSessionId_CsePrefix_ReturnsAsIs()
    {
        var result = SessionIdCompat.ToInfraSessionId("cse_abc123");
        Assert.Equal("cse_abc123", result);
    }

    [Fact]
    public void SameSessionId_CrossPrefix_ReturnsTrue()
    {
        Assert.True(SessionIdCompat.SameSessionId("cse_abc123", "session_abc123"));
        Assert.True(SessionIdCompat.SameSessionId("session_abc123", "cse_abc123"));
    }

    [Fact]
    public void SameSessionId_SamePrefix_ReturnsTrue()
    {
        Assert.True(SessionIdCompat.SameSessionId("cse_abc123", "cse_abc123"));
        Assert.True(SessionIdCompat.SameSessionId("session_abc123", "session_abc123"));
    }

    [Fact]
    public void SameSessionId_DifferentId_ReturnsFalse()
    {
        Assert.False(SessionIdCompat.SameSessionId("cse_abc123", "session_xyz789"));
    }
}
