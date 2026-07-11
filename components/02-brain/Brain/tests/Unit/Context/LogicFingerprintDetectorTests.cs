namespace Core.Context;

public sealed class LogicFingerprintDetectorTests
{
    [Fact]
    public void Record_ShortText_NoLoop()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var result = sut.Record("短文本");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_SameTextTwice_NotDetectedWithDefaultThreshold()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var text = new string('A', 100);
        sut.Record(text);
        var result = sut.Record(text);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_SameTextFourTimes_DetectedWithDefaultThreshold()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var text = new string('A', 100);
        sut.Record(text);
        sut.Record(text);
        sut.Record(text);
        var result = sut.Record(text);
        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void Record_DifferentText_NoLoop()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, windowSize: 5, hitThreshold: 2);
        var text1 = new string('A', 100);
        var text2 = new string('B', 100);
        sut.Record(text1);
        var result = sut.Record(text2);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_SamePrefixSuffix_DifferentMiddle_Detected()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, windowSize: 5, hitThreshold: 2);
        var text1 = "让我分析这段代码的实现方式，中间内容完全不同但是首尾一样，结论是需要重构";
        var text2 = "让我分析这段代码的实现方式，中间内容差异很大但是首尾一样，结论是需要重构";
        sut.Record(text1);
        var result = sut.Record(text2);
        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void Record_ThreeHits_CountsCorrectly()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, windowSize: 5, hitThreshold: 2);
        var text = new string('X', 100);
        sut.Record(text);
        sut.Record(text);
        var result = sut.Record(text);
        Assert.True(result.IsLoopDetected);
        Assert.Equal(3, result.HitCount);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, hitThreshold: 2);
        var text = new string('A', 100);
        sut.Record(text);
        sut.Record(text);
        sut.Reset();
        var result = sut.Record(text);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_TriggerCount_Increments()
    {
        var sut = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, hitThreshold: 2);
        var text = new string('A', 100);
        sut.Record(text);
        var result1 = sut.Record(text);
        Assert.Equal(1, result1.TriggerCount);
        var result2 = sut.Record(text);
        Assert.Equal(2, result2.TriggerCount);
    }

    [Fact]
    public void NoLoop_StaticProperty_HasDefaults()
    {
        Assert.False(LogicFingerprintResult.NoLoop.IsLoopDetected);
        Assert.Equal(0, LogicFingerprintResult.NoLoop.HitCount);
    }
}
