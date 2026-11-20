namespace MockServer.Core.Tests;

/// <summary>
/// 浏览器自动打开策略 — 测试/自动化宿主（输出或输入重定向）不得弹出浏览器 Dashboard。
/// </summary>
public sealed class BrowserLaunchPolicyTests {
    [Fact]
    public void ShouldOpen_InteractiveTerminal_Opens() {
        BrowserLaunchPolicy.ShouldOpen(noOpenBrowser: false, outputRedirected: false, inputRedirected: false)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldOpen_ExplicitNoOpenBrowser_DoesNotOpen() {
        BrowserLaunchPolicy.ShouldOpen(noOpenBrowser: true, outputRedirected: false, inputRedirected: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldOpen_OutputRedirected_DoesNotOpen() {
        BrowserLaunchPolicy.ShouldOpen(noOpenBrowser: false, outputRedirected: true, inputRedirected: false)
            .Should().BeFalse("E2E/测试进程重定向 stdout，Dashboard 会随测试结束立即失联");
    }

    [Fact]
    public void ShouldOpen_InputRedirected_DoesNotOpen() {
        BrowserLaunchPolicy.ShouldOpen(noOpenBrowser: false, outputRedirected: false, inputRedirected: true)
            .Should().BeFalse();
    }
}
