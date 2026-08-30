namespace Mcp.Tests;

public sealed class StepUpDetectorTests
{
    [Fact]
    public void DetectStepUp_Non403_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        StepUpDetector.DetectStepUp(response, null).Should().BeNull();
    }

    [Fact]
    public void DetectStepUp_403NoWwwAuth_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        StepUpDetector.DetectStepUp(response, null).Should().BeNull();
    }

    [Fact]
    public void DetectStepUp_403WithInsufficientScope_ReturnsScope()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ParseAdd("Bearer error=\"insufficient_scope\", scope=\"read:admin\"");

        var result = StepUpDetector.DetectStepUp(response, null);
        result.Should().Be("read:admin");
    }

    [Fact]
    public void DetectStepUp_403WithInsufficientScope_NotifyAuthProvider()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ParseAdd("Bearer error=\"insufficient_scope\", scope=\"write:data\"");

        var mockProvider = new Mock<IMcpAuthProvider>();
        mockProvider.Setup(p => p.MarkStepUpPending("write:data"));

        StepUpDetector.DetectStepUp(response, mockProvider.Object);

        mockProvider.Verify(p => p.MarkStepUpPending("write:data"), Times.Once);
    }

    [Fact]
    public void DetectStepUp_403WithoutInsufficientScope_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ParseAdd("Bearer error=\"invalid_token\"");

        StepUpDetector.DetectStepUp(response, null).Should().BeNull();
    }

    [Fact]
    public void DetectStepUp_NonBearerScheme_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ParseAdd("Basic realm=\"test\"");

        StepUpDetector.DetectStepUp(response, null).Should().BeNull();
    }

    [Fact]
    public void DetectStepUp_NullResponse_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StepUpDetector.DetectStepUp(null!, null));
    }
}
