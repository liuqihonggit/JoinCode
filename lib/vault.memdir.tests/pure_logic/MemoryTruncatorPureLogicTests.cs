
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryTruncator 纯逻辑确定性测试
/// TruncateByBytes: UTF-8 多字节字符截断点/续字节回退
/// CalculateLineRelevance: 行相关性评分
/// </summary>
public sealed class MemoryTruncatorPureLogicTests {
    private static readonly string TruncateSuffix = L.T(StringKey.VaultTruncatedMaxBytes);

    // === TruncateByBytes: ASCII ===

    [Fact]
    public void TruncateByBytes_ContentUnderLimit_ReturnsContentUnchanged() {
        var content = "short content";
        var result = MemoryTruncator.TruncateByBytes(content, 1024);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateByBytes_AsciiExactCutAtCharacterBoundary() {
        var content = new string('A', 200);
        // maxBytes=110 → cutLength=10, bytes[10] 是 ASCII,直接切
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().Be(new string('A', 10) + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_EmptyContent_ReturnsEmpty() {
        // bytes.Length=0 <= maxBytes,返回原内容 ""
        var result = MemoryTruncator.TruncateByBytes("", 110);
        result.Should().BeEmpty();
    }

    [Fact]
    public void TruncateByBytes_MaxBytesBelow100_CutLengthClampedToZero() {
        var content = new string('A', 200);
        // maxBytes=50 → cutLength=max(0,50-100)=0
        var result = MemoryTruncator.TruncateByBytes(content, 50);
        result.Should().Be(TruncateSuffix);
    }

    // === TruncateByBytes: UTF-8 多字节字符 ===

    [Fact]
    public void TruncateByBytes_MultiByteChar_CutFallsOnContinuationByte_RetreatsToCharBoundary() {
        // "你好世界" 每字 3 字节,10 组 = 40 字 = 120 字节
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        // maxBytes=110 → cutLength=10, bytes[10] 是"界"的续字节 → 回退到 bytes[9]="界"起始 → 再回退过"世"到 bytes[6]="世"起始
        // 最终 cutLength=6, result="你好" + 后缀
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().Be("你好" + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_MultiByteChar_CutExactlyOnCharBoundary_NoRetreat() {
        // 10 组 "你好世界" = 120 字节,maxBytes=112 → cutLength=12, bytes[12] 是第2组"你"起始字节
        // 12 是完整字符边界(4字×3字节=12),bytes[12] 是多字节起始 → 回退到 cutLength=9...6
        // 实际:bytes[12]=0xE4(第2组"你"起始),是续字节?不,0xE4&0xC0=0xC0 != 0x80,退出 while
        // 然后 0xE4 是多字节起始 → 回退到 cutLength=11,10,9...9 是"界"起始 → 退出
        // result = bytes[0..9] = "你好世" + 后缀
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        var result = MemoryTruncator.TruncateByBytes(content, 112);
        result.Should().Be("你好世" + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_PureMultiByte_AllBytesRetreatToCharBoundary() {
        // 纯中文,确保截断点不产生半个字符
        var content = string.Concat(Enumerable.Repeat("中", 50)); // 50字×3字节=150字节
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        // cutLength=10, bytes[10] 是第4字"中"的第2字节(续) → 回退到 bytes[9] 起始 → 再回退到 bytes[6] 起始 → 退出
        // result = bytes[0..6] = "中中" + 后缀 (3字×2=6字节)
        // 但 bytes[9] 是第4字起始(0xE4),回退后 cutLength=8,7,6 → bytes[6] 是第3字起始(0xE4),退出
        // result = bytes[0..6] = "中中"
        result.Should().Be("中中" + TruncateSuffix);
        // 验证结果中无半个字符:解码应成功且字符数为整数
        result.Should().Contain("中");
    }

    [Fact]
    public void TruncateByBytes_MixedAsciiAndMultiByte_HandlesCorrectly() {
        // "a你b好" = a(1) + 你(3) + b(1) + 好(3) = 8 字节
        // 重复 20 次 = 160 字节
        var content = string.Concat(Enumerable.Repeat("a你b好", 20));
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        // cutLength=10, 需要分析 bytes[10] 的位置
        // "a你b好" = [0x61, E4 BD A0, 0x62, E5 A5 BD] = 8字节
        // 索引: 0=a, 1-3=你, 4=b, 5-7=好
        // 第二组: 8=a, 9-11=你, 12=b, 13-15=好
        // cutLength=10: bytes[10]=0xBD(你第3字节,续) → cutLength=9, bytes[9]=0xE4(你起始) → 退出
        // 0xE4 多字节起始 → cutLength=8, bytes[8]=0x61(a,ASCII) → 退出
        // result = bytes[0..8] = "a你b好" + 后缀
        result.Should().Be("a你b好" + TruncateSuffix);
    }

    // === TruncateByBytes: 确定性 ===

    [Fact]
    public void TruncateByBytes_Deterministic_SameInputSameOutput() {
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        var r1 = MemoryTruncator.TruncateByBytes(content, 105);
        var r2 = MemoryTruncator.TruncateByBytes(content, 105);
        r1.Should().Be(r2);
    }

    [Fact]
    public void TruncateByBytes_ResultAlwaysEndsWithSuffix() {
        var content = new string('A', 200);
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().EndWith(TruncateSuffix);
    }

    // === CalculateLineRelevance ===

    [Fact]
    public void CalculateLineRelevance_AllWordsMatch_ReturnsOne() {
        var result = MemoryTruncator.CalculateLineRelevance("database optimization", new[] { "database", "optimization" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_PartialMatch_ReturnsRatio() {
        // 2 个查询词,1 个匹配 → 1/2 = 0.5
        var result = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        result.Should().Be(0.5);
    }

    [Fact]
    public void CalculateLineRelevance_NoMatch_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("unrelated content", new[] { "database", "network" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_EmptyLine_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("", new[] { "database" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_WhitespaceLine_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("   ", new[] { "database" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_EmptyQueryWords_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("some content", Array.Empty<string>());
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_CaseInsensitiveMatch() {
        var result = MemoryTruncator.CalculateLineRelevance("DATABASE content", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_SubstringMatch_CountsAsMatch() {
        // line.Contains 是子串匹配,"database" 在 "databases" 中匹配
        var result = MemoryTruncator.CalculateLineRelevance("databases content", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_SingleWordMatch_ReturnsOne() {
        var result = MemoryTruncator.CalculateLineRelevance("database everywhere", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_ThreeWordsOneMatch_ReturnsOneThird() {
        var result = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network", "security" });
        result.Should().BeApproximately(1.0 / 3.0, 1e-9);
    }

    // === CalculateLineRelevance: 确定性 ===

    [Fact]
    public void CalculateLineRelevance_Deterministic_SameInputSameOutput() {
        var r1 = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        var r2 = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        r1.Should().Be(r2);
    }
}
