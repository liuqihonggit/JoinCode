
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryTypeProfiles 纯逻辑确定性测试
/// 验证各 MemoryType 的 TTL/权重/名称映射
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class MemoryTypeProfilesTests {

    // === GetName ===

    [Fact]
    public void GetName_User_ReturnsUserString() {
        MemoryType.User.GetName().Should().Be("User");
    }

    [Fact]
    public void GetName_Feedback_ReturnsFeedbackString() {
        MemoryType.Feedback.GetName().Should().Be("Feedback");
    }

    [Fact]
    public void GetName_Project_ReturnsProjectString() {
        MemoryType.Project.GetName().Should().Be("Project");
    }

    [Fact]
    public void GetName_Reference_ReturnsReferenceString() {
        MemoryType.Reference.GetName().Should().Be("Reference");
    }

    // === GetDefaultTtl ===

    [Fact]
    public void GetDefaultTtl_User_Returns365Days() {
        MemoryType.User.GetDefaultTtl().Should().Be(TimeSpan.FromDays(365));
    }

    [Fact]
    public void GetDefaultTtl_Feedback_Returns180Days() {
        MemoryType.Feedback.GetDefaultTtl().Should().Be(TimeSpan.FromDays(180));
    }

    [Fact]
    public void GetDefaultTtl_Project_Returns90Days() {
        MemoryType.Project.GetDefaultTtl().Should().Be(TimeSpan.FromDays(90));
    }

    [Fact]
    public void GetDefaultTtl_Reference_Returns30Days() {
        MemoryType.Reference.GetDefaultTtl().Should().Be(TimeSpan.FromDays(30));
    }

    // === GetBaseRelevanceWeight ===

    [Fact]
    public void GetBaseRelevanceWeight_User_ReturnsOne() {
        MemoryType.User.GetBaseRelevanceWeight().Should().Be(1.0);
    }

    [Fact]
    public void GetBaseRelevanceWeight_Feedback_ReturnsZeroPointNine() {
        MemoryType.Feedback.GetBaseRelevanceWeight().Should().Be(0.9);
    }

    [Fact]
    public void GetBaseRelevanceWeight_Project_ReturnsZeroPointEight() {
        MemoryType.Project.GetBaseRelevanceWeight().Should().Be(0.8);
    }

    [Fact]
    public void GetBaseRelevanceWeight_Reference_ReturnsZeroPointSix() {
        MemoryType.Reference.GetBaseRelevanceWeight().Should().Be(0.6);
    }

    // === 权重排序语义 ===

    [Fact]
    public void GetBaseRelevanceWeight_UserHigherThanFeedback() {
        MemoryType.User.GetBaseRelevanceWeight().Should().BeGreaterThan(MemoryType.Feedback.GetBaseRelevanceWeight());
    }

    [Fact]
    public void GetBaseRelevanceWeight_FeedbackHigherThanProject() {
        MemoryType.Feedback.GetBaseRelevanceWeight().Should().BeGreaterThan(MemoryType.Project.GetBaseRelevanceWeight());
    }

    [Fact]
    public void GetBaseRelevanceWeight_ProjectHigherThanReference() {
        MemoryType.Project.GetBaseRelevanceWeight().Should().BeGreaterThan(MemoryType.Reference.GetBaseRelevanceWeight());
    }

    // === TTL 排序语义 ===

    [Fact]
    public void GetDefaultTtl_UserLongestThanFeedback() {
        MemoryType.User.GetDefaultTtl().Should().BeGreaterThan(MemoryType.Feedback.GetDefaultTtl());
    }

    [Fact]
    public void GetDefaultTtl_ReferenceShortest() {
        var referenceTtl = MemoryType.Reference.GetDefaultTtl();
        MemoryType.User.GetDefaultTtl().Should().BeGreaterThan(referenceTtl);
        MemoryType.Feedback.GetDefaultTtl().Should().BeGreaterThan(referenceTtl);
        MemoryType.Project.GetDefaultTtl().Should().BeGreaterThan(referenceTtl);
    }

    // === 确定性 ===

    [Fact]
    public void GetDefaultTtl_Deterministic_SameTypeSameTtl() {
        MemoryType.User.GetDefaultTtl().Should().Be(MemoryType.User.GetDefaultTtl());
    }

    [Fact]
    public void GetBaseRelevanceWeight_Deterministic_SameTypeSameWeight() {
        MemoryType.User.GetBaseRelevanceWeight().Should().Be(MemoryType.User.GetBaseRelevanceWeight());
    }
}
