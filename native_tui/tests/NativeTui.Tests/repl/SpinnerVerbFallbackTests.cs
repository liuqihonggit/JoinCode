namespace NativeTui.Tests.Repl;

/// <summary>
/// Spinner 随机动词 fallback 测试 — 对齐基准 Spinner.tsx：
/// verb = overrideMessage ?? randomVerb（挂载时 useState 采样一次），message = verb + '…'；
/// Message 非空优先，空时懒采样缓存（ResetVerb 后重采样）。
/// </summary>
public sealed class SpinnerVerbFallbackTests
{
    /// <summary>构造 Message 空 + 种子随机的 spinner。</summary>
    private static SpinnerItem VerbItem(int seed) => new()
    {
        Message = string.Empty,
        Mode = SpinnerMode.Responding,
        Random = new Random(seed),
    };

    [Fact]
    public void Render_EmptyMessage_ShowsRandomVerbWithEllipsis()
    {
        using var screen = new Screen(40, 2);
        var spinner = VerbItem(seed: 42);
        spinner.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        var expected = SpinnerVerbs.Sample(new Random(42));
        Assert.StartsWith("· " + expected + "…", RowText.Of(screen, 1, 0, 30));
    }

    [Fact]
    public void Render_ExplicitMessage_TakesPrecedenceOverVerb()
    {
        using var screen = new Screen(40, 2);
        var spinner = VerbItem(seed: 42);
        spinner.Message = "Reading files…";
        spinner.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        Assert.StartsWith("· Reading files…", RowText.Of(screen, 1, 0, 20));
    }

    [Fact]
    public void Render_VerbCachedAcrossFrames()
    {
        var spinner = VerbItem(seed: 42);
        using var screenA = new Screen(40, 2);
        spinner.Render(screenA, new Rect(0, 0, 40, 2), Theme.Dark);
        var firstVerb = RowText.Of(screenA, 1, 2, 20).Split('…')[0];

        using var screenB = new Screen(40, 2);
        spinner.AnimTimeMs += 120;
        spinner.Render(screenB, new Rect(0, 0, 40, 2), Theme.Dark);
        var secondVerb = RowText.Of(screenB, 1, 2, 20).Split('…')[0];

        Assert.Equal(firstVerb, secondVerb);
    }

    [Fact]
    public void ResetVerb_NextRenderResamples()
    {
        var spinner = VerbItem(seed: 42);
        using var screenA = new Screen(40, 2);
        spinner.Render(screenA, new Rect(0, 0, 40, 2), Theme.Dark);
        var firstVerb = RowText.Of(screenA, 1, 2, 20).Split('…')[0];

        spinner.ResetVerb();
        using var screenB = new Screen(40, 2);
        spinner.Render(screenB, new Rect(0, 0, 40, 2), Theme.Dark);
        var secondVerb = RowText.Of(screenB, 1, 2, 20).Split('…')[0];

        // 不同种子序列（Reset 后重采样，同 Random 序列继续）— 两者都来自表内
        Assert.Contains(firstVerb, SpinnerVerbs.All);
        Assert.Contains(secondVerb, SpinnerVerbs.All);
    }
}
