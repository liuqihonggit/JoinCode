namespace NativeTui.Tests.Repl;

/// <summary>
/// Spinner 动词表测试 — 对齐基准 constants/spinnerVerbs.ts：
/// 187 个动词（唯一数据源，禁止编造）、Sample 随机选 + "Working" 兜底。
/// </summary>
public sealed class SpinnerVerbsTests
{
    [Fact]
    public void All_Has187Verbs()
    {
        Assert.Equal(187, SpinnerVerbs.All.Length);
    }

    [Fact]
    public void All_NoEmptyOrDuplicateEntries()
    {
        Assert.All(SpinnerVerbs.All, verb => Assert.False(string.IsNullOrWhiteSpace(verb)));
        Assert.Equal(SpinnerVerbs.All.Length, SpinnerVerbs.All.Distinct().Count());
    }

    [Fact]
    public void All_ContainsBenchmarkSignatures()
    {
        Assert.Contains("Accomplishing", SpinnerVerbs.All);
        Assert.Contains("Clauding", SpinnerVerbs.All);
        Assert.Contains("Reticulating", SpinnerVerbs.All);
        Assert.Contains("Thinking", SpinnerVerbs.All);
        Assert.Contains("Working", SpinnerVerbs.All);
    }

    [Fact]
    public void Sample_ReturnsVerbFromTable()
    {
        var random = new Random(42);
        for (var i = 0; i < 20; i++)
        {
            var verb = SpinnerVerbs.Sample(random);
            Assert.Contains(verb, SpinnerVerbs.All);
        }
    }

    [Fact]
    public void Sample_EmptyTable_FallsBackToWorking()
    {
        Assert.Equal("Working", SpinnerVerbs.Sample(Random.Shared, []));
    }

    [Fact]
    public void Sample_DeterministicWithSameSeed()
    {
        var first = SpinnerVerbs.Sample(new Random(7));
        var second = SpinnerVerbs.Sample(new Random(7));
        Assert.Equal(first, second);
    }
}
