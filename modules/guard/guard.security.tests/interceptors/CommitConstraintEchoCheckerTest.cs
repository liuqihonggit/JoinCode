namespace Guard.Security.Tests;

/// <summary>
/// CommitConstraintEchoChecker 单元测试 — 验证 commit 消息约束回显检测的正例/反例/边界。
/// <para>
/// 覆盖三类正则模式：圆括号关键词、方括号关键词、直接关键词。
/// </para>
/// </summary>
public class CommitConstraintEchoCheckerTest {
    #region 正例 — 圆括号内含约束关键词

    [Theory]
    [InlineData("feat: xxx (不含分支名)")]
    [InlineData("feat: xxx (不放敏感信息)")]
    [InlineData("feat: xxx (不删旧文件)")]
    [InlineData("feat: xxx (不提无关内容)")]
    [InlineData("feat: xxx (无分支版)")]
    [InlineData("feat: xxx (非完整版)")]
    [InlineData("feat: xxx (已按要求隐匿某约束)")]
    [InlineData("feat: xxx (省略若干细节)")]
    public void ContainsConstraintEcho_ParenWithKeyword_ReturnsTrue(string message) {
        CommitConstraintEchoChecker.ContainsConstraintEcho(message).Should().BeTrue(
            $"commit 消息 '{message}' 含圆括号约束回显，应被拦截");
    }

    #endregion

    #region 正例 — 方括号内含约束关键词

    [Theory]
    [InlineData("feat: xxx [branch-free]")]
    [InlineData("feat: xxx [无分支]")]
    [InlineData("feat: xxx [非完整]")]
    [InlineData("feat: xxx [不含敏感]")]
    public void ContainsConstraintEcho_BracketWithKeyword_ReturnsTrue(string message) {
        CommitConstraintEchoChecker.ContainsConstraintEcho(message).Should().BeTrue(
            $"commit 消息 '{message}' 含方括号约束回显，应被拦截");
    }

    #endregion

    #region 正例 — 直接约束关键词

    [Theory]
    [InlineData("feat: 按用户要求调整 xxx")]
    [InlineData("feat: 面向领导汇报")]
    [InlineData("feat: 给领导看")]
    [InlineData("按用户要求重构")]
    public void ContainsConstraintEcho_DirectKeyword_ReturnsTrue(string message) {
        CommitConstraintEchoChecker.ContainsConstraintEcho(message).Should().BeTrue(
            $"commit 消息 '{message}' 含直接约束回显关键词，应被拦截");
    }

    #endregion

    #region 反例 — 正常 commit 消息不应匹配

    [Theory]
    [InlineData("feat: 添加新功能")]
    [InlineData("fix: 修复边界问题")]
    [InlineData("refactor: 重构代码结构")]
    [InlineData("docs: 完善文档")]
    [InlineData("chore: 更新依赖版本")]
    [InlineData("test: 补充单元测试")]
    [InlineData("perf: 优化启动性能")]
    [InlineData("feat: 支持多语言")]
    public void ContainsConstraintEcho_NormalMessage_ReturnsFalse(string message) {
        CommitConstraintEchoChecker.ContainsConstraintEcho(message).Should().BeFalse(
            $"正常 commit 消息 '{message}' 不应被误判为约束回显");
    }

    #endregion

    #region 边界 — null/空/空白

    [Fact]
    public void ContainsConstraintEcho_Null_ReturnsFalse() {
        CommitConstraintEchoChecker.ContainsConstraintEcho(null).Should().BeFalse();
    }

    [Fact]
    public void ContainsConstraintEcho_Empty_ReturnsFalse() {
        CommitConstraintEchoChecker.ContainsConstraintEcho(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void ContainsConstraintEcho_Whitespace_ReturnsFalse() {
        CommitConstraintEchoChecker.ContainsConstraintEcho("   ").Should().BeFalse();
    }

    #endregion

    #region BuildDenyReason

    [Fact]
    public void BuildDenyReason_ContainsGuidanceText() {
        var reason = CommitConstraintEchoChecker.BuildDenyReason();

        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("拦截");
        reason.Should().Contain("错误示例");
        reason.Should().Contain("正确示例");
        reason.Should().Contain("commit 消息");
    }

    [Fact]
    public void BuildDenyReason_IsDeterministic() {
        // 多次调用应返回相同内容（纯静态文本）
        var first = CommitConstraintEchoChecker.BuildDenyReason();
        var second = CommitConstraintEchoChecker.BuildDenyReason();

        first.Should().Be(second);
    }

    #endregion
}
