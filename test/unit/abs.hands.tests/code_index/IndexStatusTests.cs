namespace Abs.Tests.CodeIndex;

/// <summary>
/// IndexStatus 枚举测试 — 值覆盖、默认值。
/// </summary>
public sealed class IndexStatusTests {

    [Fact]
    public void IndexStatus_HasFourValues() {
        var values = Enum.GetValues<IndexStatus>();
        values.Should().HaveCount(4);
    }

    [Fact]
    public void IndexStatus_ContainsExpectedValues() {
        Enum.IsDefined(IndexStatus.NotReady).Should().BeTrue();
        Enum.IsDefined(IndexStatus.Ready).Should().BeTrue();
        Enum.IsDefined(IndexStatus.Partial).Should().BeTrue();
        Enum.IsDefined(IndexStatus.Error).Should().BeTrue();
    }

    [Fact]
    public void IndexStatus_DefaultIsNotReady() {
        IndexStatus defaultStatus = default;
        defaultStatus.Should().Be(IndexStatus.NotReady);
    }
}
