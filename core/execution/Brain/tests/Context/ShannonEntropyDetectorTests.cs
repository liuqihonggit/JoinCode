namespace Core.Context;

public sealed class ShannonEntropyDetectorTests
{
    [Fact]
    public void Record_ShortText_ReturnsNoLoop()
    {
        var sut = new ShannonEntropyDetector();
        var result = sut.Record("短文本");

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_NormalText_ReturnsNoLoop()
    {
        var sut = new ShannonEntropyDetector(windowSize: 10, declineThreshold: 4, minEntropyDelta: 0.05);
        var text = "这是一段正常的文本内容，包含了各种不同的字符和词汇。";

        var result = sut.Record(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_EntropyDecline_Detected()
    {
        var sut = new ShannonEntropyDetector(windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001);

        var highEntropy = string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));
        var mediumEntropy = string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));
        var lowEntropy = new string('a', 30) + new string('b', 10);
        var veryLow = new string('a', 90) + new string('b', 10);

        sut.Record(highEntropy);
        sut.Record(mediumEntropy);
        sut.Record(lowEntropy);

        var result = sut.Record(veryLow);

        Assert.True(result.IsLoopDetected);
        Assert.True(result.DeclineStreak >= 3);
    }

    [Fact]
    public void Record_StableEntropy_NoLoop()
    {
        var sut = new ShannonEntropyDetector(windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.05);

        var text = "这是一段正常的文本内容，包含了各种不同的字符。";
        for (var i = 0; i < 10; i++)
        {
            var result = sut.Record(text);
            Assert.False(result.IsLoopDetected);
        }
    }

    [Fact]
    public void Record_EntropyIncrease_NoLoop()
    {
        var sut = new ShannonEntropyDetector(windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.01);

        var low = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var medium = "aaaaabbbbbcccccdddddeeeeefffffggggghhhhh";
        var high = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()";

        sut.Record(low);
        sut.Record(medium);
        var result = sut.Record(high);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var sut = new ShannonEntropyDetector(windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.01);

        var high = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()";
        var low = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        sut.Record(high);
        sut.Record(high);
        sut.Record(low);
        var result1 = sut.Record(low);

        sut.Reset();

        var result2 = sut.Record(low);
        Assert.False(result2.IsLoopDetected);
    }

    [Fact]
    public void Record_CurrentEntropy_ReturnedCorrectly()
    {
        var sut = new ShannonEntropyDetector();
        var text = "这是一段正常的文本内容，包含了各种不同的字符。";

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy > 0);
    }

    [Fact]
    public void Record_PureRepeatingChars_LowEntropy()
    {
        var sut = new ShannonEntropyDetector();
        var text = new string('A', 100);

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy < 0.01);
    }

    [Fact]
    public void Record_UniformChars_HighEntropy()
    {
        var sut = new ShannonEntropyDetector();
        var text = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy > 4.0);
    }

    [Fact]
    public void Record_NullText_Throws()
    {
        var sut = new ShannonEntropyDetector();
        Assert.Throws<ArgumentNullException>(() => sut.Record(null!));
    }

    [Fact]
    public void NoLoop_StaticProperty_HasDefaultValues()
    {
        Assert.False(ShannonEntropyResult.NoLoop.IsLoopDetected);
        Assert.Equal(0, ShannonEntropyResult.NoLoop.CurrentEntropy);
        Assert.Equal(0, ShannonEntropyResult.NoLoop.DeclineStreak);
    }
}
