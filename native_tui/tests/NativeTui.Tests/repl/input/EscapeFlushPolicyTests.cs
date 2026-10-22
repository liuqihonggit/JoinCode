namespace NativeTui.Tests.Repl;

/// <summary>
/// 不完整前缀超时策略测试 — 基准 App.tsx:120-121（NORMAL_TIMEOUT=50ms / PASTE_TIMEOUT=500ms）：
/// 序列后半段永不到达（慢链路/终端异常）时按形态阈值判定强制 flush，释放输入通道。
/// </summary>
public sealed class EscapeFlushPolicyTests
{
    /// <summary>普通 ESC 前缀超 50ms — 触发强制 flush。</summary>
    [Theory]
    [InlineData("\u001b[", 51)]
    [InlineData("\u001b[1;", 60)]
    [InlineData("\u001bO", 100)]
    public void NormalPrefix_Beyond50Ms_Triggers(string pending, long stuckMs)
    {
        Assert.True(EscapeFlushPolicy.ShouldForceFlush(pending, stuckMs));
    }

    /// <summary>普通 ESC 前缀未超 50ms — 不触发（等待凑齐窗口）。</summary>
    [Fact]
    public void NormalPrefix_Within50Ms_DoesNotTrigger()
    {
        Assert.False(EscapeFlushPolicy.ShouldForceFlush("\u001b[1;", 49));
    }

    /// <summary>粘贴前缀（200~ 已完整出现、终止标记未到）— 500ms 阈值（大块粘贴网络传输更慢）。</summary>
    [Fact]
    public void PastePrefix_Uses500MsThreshold()
    {
        Assert.False(EscapeFlushPolicy.ShouldForceFlush("\u001b[200~partial", 499));
        Assert.True(EscapeFlushPolicy.ShouldForceFlush("\u001b[200~partial", 501));
    }

    /// <summary>非 ESC 开头（普通文本滞留）— 不触发（文本解析永成功，滞留仅 ESC 形态）。</summary>
    [Fact]
    public void NonEscapePrefix_NeverTriggers()
    {
        Assert.False(EscapeFlushPolicy.ShouldForceFlush("abc", 999_999));
    }
}
