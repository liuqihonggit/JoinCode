namespace Core.Context;

/// <summary>
/// LoopInterventionOptions 及 Builder 单元测试
/// </summary>
public sealed class LoopInterventionOptionsTests
{
    [Fact]
    public void Defaults_AreExpectedValues()
    {
        var options = new LoopInterventionOptions();

        options.HardTruncateThreshold.Should().Be(3);
        options.CompactThreshold.Should().Be(5);
        options.MaxRetryAttempts.Should().Be(2);
        options.RetryTemperature.Should().Be(0.6f);
        options.ProgressDiscount.Should().Be(1);
        options.SecondChanceTemperature.Should().Be(0.3f);
        options.InsertRewindAuditMark.Should().BeTrue();
        options.PreserveLastUserMessageOnReset.Should().BeTrue();
        options.CompactFoldDecision.Should().Be(ContextFoldDecision.FoldAggressive);
        options.SoftIntervenePrompt.Should().NotBeNullOrEmpty();
        options.HardTruncatePrompt.Should().NotBeNullOrEmpty();
        options.CompactPrompt.Should().NotBeNullOrEmpty();
        options.CompactSuccessPrompt.Should().NotBeNullOrEmpty();
        options.CompactFallbackPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Builder_Create_ReturnsNewInstance()
    {
        var options = LoopInterventionOptionsBuilder.Create().Build();

        options.Should().NotBeNull();
        options.HardTruncateThreshold.Should().Be(3);
    }

    [Fact]
    public void Builder_WithHardTruncateThreshold_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithHardTruncateThreshold(10)
            .Build();

        options.HardTruncateThreshold.Should().Be(10);
    }

    [Fact]
    public void Builder_WithCompactThreshold_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithCompactThreshold(8)
            .Build();

        options.CompactThreshold.Should().Be(8);
    }

    [Fact]
    public void Builder_WithMaxRetryAttempts_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithMaxRetryAttempts(5)
            .Build();

        options.MaxRetryAttempts.Should().Be(5);
    }

    [Fact]
    public void Builder_WithRetryTemperature_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithRetryTemperature(0.9f)
            .Build();

        options.RetryTemperature.Should().Be(0.9f);
    }

    [Fact]
    public void Builder_WithSoftIntervenePrompt_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithSoftIntervenePrompt("custom prompt")
            .Build();

        options.SoftIntervenePrompt.Should().Be("custom prompt");
    }

    [Fact]
    public void Builder_WithCompactFoldDecision_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithCompactFoldDecision(ContextFoldDecision.FoldNormal)
            .Build();

        options.CompactFoldDecision.Should().Be(ContextFoldDecision.FoldNormal);
    }

    [Fact]
    public void Builder_WithProgressDiscount_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithProgressDiscount(2)
            .Build();

        options.ProgressDiscount.Should().Be(2);
    }

    [Fact]
    public void Builder_WithSecondChanceTemperature_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithSecondChanceTemperature(0.1f)
            .Build();

        options.SecondChanceTemperature.Should().Be(0.1f);
    }

    [Fact]
    public void Builder_WithInsertRewindAuditMark_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithInsertRewindAuditMark(false)
            .Build();

        options.InsertRewindAuditMark.Should().BeFalse();
    }

    [Fact]
    public void Builder_WithPreserveLastUserMessageOnReset_SetsValue()
    {
        var options = LoopInterventionOptionsBuilder.Create()
            .WithPreserveLastUserMessageOnReset(false)
            .Build();

        options.PreserveLastUserMessageOnReset.Should().BeFalse();
    }
}
