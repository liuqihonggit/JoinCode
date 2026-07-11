namespace Core.Context;

public sealed class ToolCallSequenceDetectorTests
{
    [Fact]
    public void Record_SingleToolCall_NoLoop()
    {
        var sut = new ToolCallSequenceDetector();
        var result = sut.Record("Read");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_TwoDifferentTools_NoLoop()
    {
        var sut = new ToolCallSequenceDetector();
        sut.Record("Read");
        var result = sut.Record("Grep");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_RepeatPattern_Detected()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        var result = sut.Record("Grep");
        Assert.True(result.IsLoopDetected);
        Assert.Equal(2, result.RepeatCount);
        Assert.Equal("Read→Grep", result.RepeatedPattern);
    }

    [Fact]
    public void Record_DefaultThreshold_FourRepeats_Detected()
    {
        var sut = new ToolCallSequenceDetector();
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Edit");
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Edit");
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Edit");
        sut.Record("Read");
        sut.Record("Grep");
        var result = sut.Record("Edit");
        Assert.True(result.IsLoopDetected);
        Assert.Equal("Read→Grep→Edit", result.RepeatedPattern);
    }

    [Fact]
    public void Record_DefaultThreshold_TwoRepeats_NotDetected()
    {
        var sut = new ToolCallSequenceDetector();
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        var result = sut.Record("Grep");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_ThreeRepeats_Detected()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        var result = sut.Record("Grep");
        Assert.True(result.IsLoopDetected);
        Assert.Equal(3, result.RepeatCount);
    }

    [Fact]
    public void Record_SameToolRepeated_DetectsPairPattern()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Read");
        sut.Record("Read");
        var result = sut.Record("Read");
        Assert.True(result.IsLoopDetected);
        Assert.Equal("Read→Read", result.RepeatedPattern);
    }

    [Fact]
    public void Record_SameToolRepeated_DetectedWithMinPattern1()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 1, requiredRepeats: 3);
        sut.Record("Read");
        sut.Record("Read");
        var result = sut.Record("Read");
        Assert.True(result.IsLoopDetected);
        Assert.Equal("Read", result.RepeatedPattern);
    }

    [Fact]
    public void Record_DifferentToolsAfterLoop_NoFalsePositive()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        sut.Record("Grep");
        var result = sut.Record("Write");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Read");
        sut.Record("Grep");
        sut.Reset();
        var result = sut.Record("Read");
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_LongPattern_Detected()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 3, requiredRepeats: 2);
        sut.Record("Read");
        sut.Record("Grep");
        sut.Record("Write");
        sut.Record("Read");
        sut.Record("Grep");
        var result = sut.Record("Write");
        Assert.True(result.IsLoopDetected);
        Assert.Equal("Read→Grep→Write", result.RepeatedPattern);
    }

    [Fact]
    public void NoLoop_StaticProperty_HasDefaults()
    {
        Assert.False(ToolCallSequenceResult.NoLoop.IsLoopDetected);
        Assert.Null(ToolCallSequenceResult.NoLoop.RepeatedPattern);
        Assert.Equal(0, ToolCallSequenceResult.NoLoop.RepeatCount);
        Assert.False(ToolCallSequenceResult.NoLoop.ArgsMatched);
    }

    [Fact]
    public void Record_WithArgsFingerprint_ArgsMatched_Detected()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read", "Read(file.py)");
        sut.Record("Grep", "Grep(pattern)");
        sut.Record("Read", "Read(file.py)");
        var result = sut.Record("Grep", "Grep(pattern)");
        Assert.True(result.IsLoopDetected);
        Assert.True(result.ArgsMatched);
        Assert.Equal(2, result.TriggerCount);
    }

    [Fact]
    public void Record_WithArgsFingerprint_ArgsNotMatched_StillDetectedButTriggerCountDowngraded()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 3);
        sut.Record("Read", "Read(file1.py)");
        sut.Record("Grep", "Grep(pattern1)");
        sut.Record("Read", "Read(file2.py)");
        sut.Record("Grep", "Grep(pattern2)");
        sut.Record("Read", "Read(file3.py)");
        var result = sut.Record("Grep", "Grep(pattern3)");
        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(3, result.RepeatCount);
        Assert.Equal(2, result.TriggerCount);
    }

    [Fact]
    public void Record_WithArgsFingerprint_ArgsNotMatched_TwoRepeats_StillDetectedButDowngraded()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read", "Read(file1.py)");
        sut.Record("Grep", "Grep(pattern1)");
        sut.Record("Read", "Read(file2.py)");
        var result = sut.Record("Grep", "Grep(pattern2)");
        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(1, result.TriggerCount);
    }

    [Fact]
    public void Record_WithArgsFingerprint_MixedArgs_PartialMatch()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read", "Read(file.py)");
        sut.Record("Grep", "Grep(pattern1)");
        sut.Record("Read", "Read(file.py)");
        var result = sut.Record("Grep", "Grep(pattern2)");
        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(2, result.RepeatCount);
        Assert.Equal(1, result.TriggerCount);
    }

    [Fact]
    public void Record_WithNullFingerprint_TreatedAsNoArgs()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 2, requiredRepeats: 2);
        sut.Record("Read", null);
        sut.Record("Grep", null);
        sut.Record("Read", null);
        var result = sut.Record("Grep", null);
        Assert.True(result.IsLoopDetected);
        Assert.True(result.ArgsMatched);
    }

    [Fact]
    public void Record_WithArgsFingerprint_AllArgsSame_TriggerCountEqualsRepeatCount()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 1, requiredRepeats: 3);
        sut.Record("Read", "Read(file.py)");
        sut.Record("Read", "Read(file.py)");
        var result = sut.Record("Read", "Read(file.py)");
        Assert.True(result.IsLoopDetected);
        Assert.True(result.ArgsMatched);
        Assert.Equal(3, result.TriggerCount);
        Assert.Equal(3, result.RepeatCount);
    }

    [Fact]
    public void Record_WithArgsFingerprint_DiffArgs_TriggerCountLessThanRepeatCount()
    {
        var sut = new ToolCallSequenceDetector(minPatternLength: 1, requiredRepeats: 3);
        sut.Record("Read", "Read(file1.py)");
        sut.Record("Read", "Read(file2.py)");
        sut.Record("Read", "Read(file3.py)");
        var result = sut.Record("Read", "Read(file4.py)");
        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(3, result.TriggerCount);
        Assert.Equal(4, result.RepeatCount);
    }
}
