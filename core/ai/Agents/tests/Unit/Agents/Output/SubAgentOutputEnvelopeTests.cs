namespace Core.Agents;

public sealed class SubAgentOutputEnvelopeTests
{
    [Fact]
    public void Wrap_WithSummary_ProducesXmlWithSummaryTag()
    {
        var xml = SubAgentOutputEnvelope.Wrap("agent-1", SubAgentEnvelopeState.Completed, "修复了登录bug", "done");

        xml.Should().Contain("<task id=\"agent-1\" state=\"completed\">");
        xml.Should().Contain("<summary>修复了登录bug</summary>");
        xml.Should().Contain("<task_result>");
        xml.Should().Contain("done");
        xml.Should().Contain("</task>");
    }

    [Fact]
    public void Wrap_WithoutSummary_OmitsSummaryTag()
    {
        var xml = SubAgentOutputEnvelope.Wrap("agent-2", SubAgentEnvelopeState.Completed, null, "ok");

        xml.Should().NotContain("<summary>");
        xml.Should().Contain("<task_result>");
        xml.Should().Contain("ok");
    }

    [Fact]
    public void Wrap_ErrorState_UsesErrorState()
    {
        var xml = SubAgentOutputEnvelope.Wrap("agent-3", SubAgentEnvelopeState.Error, null, "failed");

        xml.Should().Contain("state=\"error\"");
    }

    [Fact]
    public void Wrap_CompletedState_UsesCompletedState()
    {
        var xml = SubAgentOutputEnvelope.Wrap("agent-4", SubAgentEnvelopeState.Completed, null, "ok");

        xml.Should().Contain("state=\"completed\"");
    }

    [Fact]
    public void Wrap_EscapesAgentId_SpecialChars()
    {
        var xml = SubAgentOutputEnvelope.Wrap("a<b>&\"c", SubAgentEnvelopeState.Completed, null, "x");

        xml.Should().Contain("id=\"a&lt;b&gt;&amp;&quot;c\"");
    }

    [Fact]
    public void Wrap_ContentNotEscaped()
    {
        var xml = SubAgentOutputEnvelope.Wrap("agent", SubAgentEnvelopeState.Completed, null, "code < tag > here");

        xml.Should().Contain("code < tag > here");
    }

    [Fact]
    public void ExtractSummary_FirstLine_ReturnsTrimmed()
    {
        var summary = SubAgentOutputEnvelope.ExtractSummary("  修复登录  \n第二行");

        summary.Should().Be("修复登录");
    }

    [Fact]
    public void ExtractSummary_LongLine_TruncatesWithEllipsis()
    {
        var longLine = new string('a', 150);
        var summary = SubAgentOutputEnvelope.ExtractSummary(longLine);

        summary.Should().HaveLength(101);
        summary.Should().EndWith("…");
        summary.Should().StartWith(new string('a', 100));
    }

    [Fact]
    public void ExtractSummary_Empty_ReturnsNull()
    {
        SubAgentOutputEnvelope.ExtractSummary("").Should().BeNull();
    }

    [Fact]
    public void ExtractSummary_Multiline_ReturnsFirstLine()
    {
        var summary = SubAgentOutputEnvelope.ExtractSummary("第一行\n第二行\n第三行");

        summary.Should().Be("第一行");
    }

    [Fact]
    public void ExtractSummary_WhitespaceOnly_ReturnsNull()
    {
        SubAgentOutputEnvelope.ExtractSummary("   \n  ").Should().BeNull();
    }

    [Fact]
    public void ExtractSummary_ExactMaxChars_ReturnsUntruncated()
    {
        var exact = new string('b', 100);
        var summary = SubAgentOutputEnvelope.ExtractSummary(exact);

        summary.Should().Be(exact);
        summary.Should().HaveLength(100);
    }
}
