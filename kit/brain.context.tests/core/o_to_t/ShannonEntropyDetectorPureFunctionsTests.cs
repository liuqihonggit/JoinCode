namespace Core.Context;

/// <summary>
/// ShannonEntropyDetector 内部纯函数测试 — ComputeShannonEntropy + CountConsecutiveDecline
/// 改 internal 后直接测纯计算逻辑,不依赖状态机/时钟
/// </summary>
public sealed class ShannonEntropyDetectorPureFunctionsTests {
    // ---------- ComputeShannonEntropy ----------

    [Fact]
    public void ComputeShannonEntropy_EmptyString_ReturnsZero() {
        Assert.Equal(0.0, ShannonEntropyDetector.ComputeShannonEntropy(""));
    }

    [Fact]
    public void ComputeShannonEntropy_SingleRepeatedChar_ReturnsZero() {
        // 所有字符相同,p=1, -1*log2(1)=0
        Assert.Equal(0.0, ShannonEntropyDetector.ComputeShannonEntropy("aaaaaaaaaa"));
    }

    [Fact]
    public void ComputeShannonEntropy_TwoUniformChars_ReturnsOne() {
        // "ab" → p(a)=p(b)=0.5, H = -2*(0.5*log2(0.5)) = 1.0
        Assert.Equal(1.0, ShannonEntropyDetector.ComputeShannonEntropy("ab"), 0.0001);
    }

    [Fact]
    public void ComputeShannonEntropy_FourUniformChars_ReturnsTwo() {
        // "abcd" → 各 p=0.25, H = -4*(0.25*log2(0.25)) = 2.0
        Assert.Equal(2.0, ShannonEntropyDetector.ComputeShannonEntropy("abcd"), 0.0001);
    }

    [Fact]
    public void ComputeShannonEntropy_EightUniformChars_ReturnsThree() {
        // 8 个不同字符各 p=1/8, H = 3.0
        Assert.Equal(3.0, ShannonEntropyDetector.ComputeShannonEntropy("abcdefgh"), 0.0001);
    }

    [Fact]
    public void ComputeShannonEntropy_ConcentratedDistribution_LowerThanUniform() {
        var concentrated = new string('a', 90) + new string('b', 10); // 90% a, 10% b
        var uniform = "abcdefghij"; // 10 个不同字符

        var hConcentrated = ShannonEntropyDetector.ComputeShannonEntropy(concentrated);
        var hUniform = ShannonEntropyDetector.ComputeShannonEntropy(uniform);

        Assert.True(hConcentrated < hUniform);
        Assert.True(hConcentrated > 0);
    }

    [Fact]
    public void ComputeShannonEntropy_PureRepetition_NearZero() {
        var text = new string('A', 1000);

        Assert.True(ShannonEntropyDetector.ComputeShannonEntropy(text) < 0.0001);
    }

    // ---------- CountConsecutiveDecline ----------

    [Fact]
    public void CountConsecutiveDecline_EmptyHistory_ReturnsZero() {
        var sut = CreateDetector();

        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_SingleEntry_ReturnsZero() {
        var sut = CreateDetector();
        sut.Record(HighEntropyText);

        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_NoDecline_ReturnsZero() {
        var sut = CreateDetector();
        // 熵上升:低 → 高
        sut.Record(LowEntropyText);
        sut.Record(HighEntropyText);

        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_StableEntropy_ReturnsZero() {
        var sut = CreateDetector(minEntropyDelta: 0.05);
        var text = "这是一段稳定的文本内容,字符分布保持一致。";
        sut.Record(text);
        sut.Record(text);

        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_OneDecline_ReturnsOne() {
        var sut = CreateDetector(minEntropyDelta: 0.001);
        sut.Record(HighEntropyText);
        sut.Record(LowEntropyText);

        Assert.Equal(1, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_TwoConsecutiveDeclines_ReturnsTwo() {
        var sut = CreateDetector(minEntropyDelta: 0.001);
        sut.Record(HighEntropyText);
        sut.Record(MediumEntropyText);
        sut.Record(LowEntropyText);

        Assert.Equal(2, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_DeclineThenStable_BreaksAtStable() {
        var sut = CreateDetector(minEntropyDelta: 0.001);
        sut.Record(HighEntropyText);
        sut.Record(LowEntropyText);
        sut.Record(LowEntropyText); // 稳定,不下降

        // 从最新往回看:低-低=0 < delta → break,返回 0
        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    [Fact]
    public void CountConsecutiveDecline_DeclineThenRise_BreaksAtRise() {
        var sut = CreateDetector(minEntropyDelta: 0.001);
        sut.Record(HighEntropyText);
        sut.Record(LowEntropyText);
        sut.Record(HighEntropyText); // 上升

        // 从最新往回看:低-高 < 0 < delta → break,返回 0
        Assert.Equal(0, sut.CountConsecutiveDecline());
    }

    // ---------- Helpers ----------

    private static ShannonEntropyDetector CreateDetector(
        int windowSize = 10,
        int declineThreshold = 3,
        double minEntropyDelta = 0.001,
        TimeSpan? confirmationWindow = null)
        => new(windowSize, declineThreshold, minEntropyDelta,
              confirmationWindow ?? TimeSpan.FromSeconds(5));

    private static readonly string HighEntropyText =
        string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4))); // 104 chars, 高熵

    private static readonly string MediumEntropyText =
        string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8))); // 40 chars, 中熵

    private static readonly string LowEntropyText =
        new string('a', 30) + new string('b', 10); // 40 chars, 低熵
}
