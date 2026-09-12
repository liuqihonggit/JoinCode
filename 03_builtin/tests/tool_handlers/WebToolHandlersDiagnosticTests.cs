namespace Hands.Tests.ToolHandlers;

/// <summary>
/// WebToolHandlers 诊断方法单元测试
/// </summary>
public class WebToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebToolHandlers.BuildValidationErrorDiagnostic("url is required");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("url is required");
    }

    [Fact]
    public void BuildFetchFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebToolHandlers.BuildFetchFailedDiagnostic("http://example.com", 404, "Not found");
        diagnostic.Reason.Should().Be("FetchFailed");
        diagnostic.Details.Should().Contain(d => d.Key == "url" && d.Value == "http://example.com");
        diagnostic.Details.Should().Contain(d => d.Key == "statusCode" && d.Value == "404");
        diagnostic.Suggestions.Should().HaveCount(3);
    }

    [Fact]
    public void BuildRedirectDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebToolHandlers.BuildRedirectDiagnostic("http://a.com", "http://b.com", 301);
        diagnostic.Reason.Should().Be("重定向");
        diagnostic.Details.Should().Contain(d => d.Key == "redirect_url" && d.Value == "http://b.com");
        diagnostic.Suggestions.Should().ContainSingle();
    }
}
