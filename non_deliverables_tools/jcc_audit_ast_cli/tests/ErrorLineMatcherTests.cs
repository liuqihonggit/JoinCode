namespace JccAuditCli.Tests;

/// <summary>
/// ErrorLineMatcher 的单元测试 — 验证精确行、精确行+错误码、范围相交三种查询语义。
/// 重点覆盖二分查找边界（空集合/单元素/范围边界/全在范围外）。
/// </summary>
public sealed class ErrorLineMatcherTests {

    [Fact]
    public void Contains_精确行存在_返回true() {
        var matcher = new ErrorLineMatcher([(10, "CS4014"), (20, "CS0029")]);
        matcher.Contains(10).Should().BeTrue();
        matcher.Contains(20).Should().BeTrue();
    }

    [Fact]
    public void Contains_精确行不存在_返回false() {
        var matcher = new ErrorLineMatcher([(10, "CS4014")]);
        matcher.Contains(99).Should().BeFalse();
    }

    [Fact]
    public void Contains_空集合_始终返回false() {
        var matcher = new ErrorLineMatcher([]);
        matcher.Contains(1).Should().BeFalse();
    }

    [Fact]
    public void Contains_行存在且错误码匹配_返回true() {
        var matcher = new ErrorLineMatcher([(10, "CS4014")]);
        matcher.Contains(10, "CS4014").Should().BeTrue();
    }

    [Fact]
    public void Contains_行存在但错误码不匹配_返回false() {
        var matcher = new ErrorLineMatcher([(10, "CS4014")]);
        matcher.Contains(10, "CS0029").Should().BeFalse();
    }

    [Fact]
    public void Contains_行不存在_错误码重载返回false() {
        var matcher = new ErrorLineMatcher([(10, "CS4014")]);
        matcher.Contains(99, "CS4014").Should().BeFalse();
    }

    [Fact]
    public void IntersectsRange_错误行落在范围内_返回true() {
        var matcher = new ErrorLineMatcher([(15, "CS4014")]);
        matcher.IntersectsRange(10, 20).Should().BeTrue();
    }

    [Fact]
    public void IntersectsRange_错误行在范围外_返回false() {
        var matcher = new ErrorLineMatcher([(5, "CS4014"), (25, "CS0029")]);
        matcher.IntersectsRange(10, 20).Should().BeFalse();
    }

    [Fact]
    public void IntersectsRange_错误行在边界_返回true() {
        var matcher = new ErrorLineMatcher([(10, "CS4014"), (20, "CS0029")]);
        matcher.IntersectsRange(10, 20).Should().BeTrue();
    }

    [Fact]
    public void IntersectsRange_空集合_返回false() {
        var matcher = new ErrorLineMatcher([]);
        matcher.IntersectsRange(1, 100).Should().BeFalse();
    }

    [Fact]
    public void IntersectsRange_多错误行二分查找_正确判断() {
        var matcher = new ErrorLineMatcher([
            (5, "CS4014"), (10, "CS0029"), (15, "CS1503"), (20, "CS0019"), (25, "CS1929")
        ]);
        matcher.IntersectsRange(12, 18).Should().BeTrue();
        matcher.IntersectsRange(26, 30).Should().BeFalse();
        matcher.IntersectsRange(1, 4).Should().BeFalse();
    }

    [Fact]
    public void IntersectsRange_start大于所有错误行_返回false() {
        var matcher = new ErrorLineMatcher([(5, "CS4014"), (10, "CS0029")]);
        matcher.IntersectsRange(11, 20).Should().BeFalse();
    }

    [Fact]
    public void IntersectsRange_end小于所有错误行_返回false() {
        var matcher = new ErrorLineMatcher([(15, "CS4014"), (20, "CS0029")]);
        matcher.IntersectsRange(1, 14).Should().BeFalse();
    }

    [Fact]
    public void 同行多错误_保留最后一个错误码() {
        var matcher = new ErrorLineMatcher([(10, "CS4014"), (10, "CS0029")]);
        matcher.Contains(10, "CS4014").Should().BeFalse();
        matcher.Contains(10, "CS0029").Should().BeTrue();
    }
}
