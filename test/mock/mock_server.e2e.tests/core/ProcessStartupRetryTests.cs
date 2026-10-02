namespace MockServer.E2E.Tests;

/// <summary>进程故障重试分类，独立于启动时序验证。</summary>
public sealed class ProcessStartupRetryTests {
    [Theory]
    [InlineData("[GEN019] 进程退出无输出", true)]
    [InlineData("[GEN027] 等待 MockServer 就绪超时", true)]
    [InlineData("[GEN028] 等待 MockServer 就绪超时", true)]
    [InlineData("[GEN036] 脚本模式错误", false)]
    [InlineData("配置错误", false)]
    [InlineData("配置中包含 GEN027 文本", false)]
    public void RetryClassification_OnlyRecognizesTransientProcessErrors(string message, bool expected) {
        Assert.Equal(expected, CoverageTestBase.IsRetryableProcessFailure(new InvalidOperationException(message)));
    }
}
