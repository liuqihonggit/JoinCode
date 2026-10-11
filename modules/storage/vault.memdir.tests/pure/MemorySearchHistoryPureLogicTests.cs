
namespace Core.Tests.Memdir;

/// <summary>
/// MemorySearchHistoryService.IsQueryRelated 纯逻辑确定性测试
/// 算法: ExtractWords(minLength=2) → overlap/minCount &gt; 0.3
/// 注意: minLength=2 表示 token.Length &gt; 2,即长度 ≥ 3 的词才保留
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class MemorySearchHistoryPureLogicTests {

    // === 完全重叠 ===

    [Fact]
    public void IsQueryRelated_IdenticalQueries_ReturnsTrue() {
        var result = MemorySearchHistoryService.IsQueryRelated("database optimization", "database optimization");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsQueryRelated_CaseInsensitiveOverlap_ReturnsTrue() {
        var result = MemorySearchHistoryService.IsQueryRelated("Database Optimization", "DATABASE optimization");
        result.Should().BeTrue();
    }

    // === 无重叠 ===

    [Fact]
    public void IsQueryRelated_NoOverlap_ReturnsFalse() {
        var result = MemorySearchHistoryService.IsQueryRelated("database optimization", "network security");
        result.Should().BeFalse();
    }

    // === 30% 阈值边界 ===

    [Fact]
    public void IsQueryRelated_ExactlyThirtyPercent_ReturnsFalse_StrictGreaterThan() {
        // 10 词,3 词重叠 → 3/10 = 0.3, 0.3 > 0.3 为 false(严格大于)
        var q1 = "aaa bbb ccc ddd eee fff ggg hhh iii jjj";
        var q2 = "aaa bbb ccc kkk lll mmm nnn ooo ppp qqq";
        var result = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        result.Should().BeFalse();
    }

    [Fact]
    public void IsQueryRelated_AboveThirtyPercent_ReturnsTrue() {
        // 10 词,4 词重叠 → 4/10 = 0.4 > 0.3
        var q1 = "aaa bbb ccc ddd eee fff ggg hhh iii jjj";
        var q2 = "aaa bbb ccc ddd kkk lll mmm nnn ooo ppp";
        var result = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsQueryRelated_BelowThirtyPercent_ReturnsFalse() {
        // 10 词,2 词重叠 → 2/10 = 0.2 < 0.3
        var q1 = "aaa bbb ccc ddd eee fff ggg hhh iii jjj";
        var q2 = "aaa bbb kkk lll mmm nnn ooo ppp qqq rrr";
        var result = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        result.Should().BeFalse();
    }

    // === minCount 取两边较小值 ===

    [Fact]
    public void IsQueryRelated_UnequalWordCounts_UsesMinCount() {
        // 5 词 vs 2 词,2 词全重叠 → 2/min(5,2)=2/2=1.0 > 0.3
        var q1 = "database optimization performance tuning index";
        var q2 = "database optimization";
        var result = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsQueryRelated_SmallQueryPartialOverlap_UsesMinCount() {
        // 5 词 vs 2 词,1 词重叠 → 1/min(5,2)=1/2=0.5 > 0.3
        var q1 = "database optimization performance tuning index";
        var q2 = "database network";
        var result = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        result.Should().BeTrue();
    }

    // === 空查询/短词过滤 ===

    [Fact]
    public void IsQueryRelated_EmptyQuery1_ReturnsFalse() {
        var result = MemorySearchHistoryService.IsQueryRelated("", "database optimization");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsQueryRelated_EmptyQuery2_ReturnsFalse() {
        var result = MemorySearchHistoryService.IsQueryRelated("database optimization", "");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsQueryRelated_BothEmpty_ReturnsFalse() {
        var result = MemorySearchHistoryService.IsQueryRelated("", "");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsQueryRelated_ShortWordsFiltered_OutByMinLength() {
        // 长度 ≤ 2 的词被过滤。"ab cd" vs "ab cd" → 两个词都被过滤 → 空集 → false
        var result = MemorySearchHistoryService.IsQueryRelated("ab cd", "ab cd");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsQueryRelated_ThreeCharWords_PreservedByMinLength() {
        // 长度 3 的词保留。"abc def" vs "abc def" → 完全重叠 → true
        var result = MemorySearchHistoryService.IsQueryRelated("abc def", "abc def");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsQueryRelated_MixedShortAndLongWords_OnlyLongOnesCount() {
        // "ab abc" vs "ab abc" → "ab" 被过滤,只留 "abc" → 1/1=1.0 → true
        var result = MemorySearchHistoryService.IsQueryRelated("ab abc", "ab abc");
        result.Should().BeTrue();
    }

    // === 确定性 ===

    [Fact]
    public void IsQueryRelated_Deterministic_SameInputSameOutput() {
        var q1 = "database optimization performance";
        var q2 = "database optimization";
        var r1 = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        var r2 = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        r1.Should().Be(r2);
    }

    [Fact]
    public void IsQueryRelated_Symmetric_OverlapIsCommutative() {
        var q1 = "database optimization performance tuning";
        var q2 = "database optimization network security";
        // overlap/minCount 不一定对称(minCount 取两边较小值),但 overlap 对称
        var r12 = MemorySearchHistoryService.IsQueryRelated(q1, q2);
        var r21 = MemorySearchHistoryService.IsQueryRelated(q2, q1);
        // minCount 相同(取较小值),所以结果对称
        r12.Should().Be(r21);
    }
}
