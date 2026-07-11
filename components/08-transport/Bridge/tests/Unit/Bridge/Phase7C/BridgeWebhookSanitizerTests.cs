
namespace Bridge.Tests.Phase7C;

public sealed class BridgeWebhookSanitizerTests
{
    [Fact]
    public void SanitizeWebhookPayload_ReturnsSameValue()
    {
        var input = new { name = "test", value = 42 };
        var result = BridgeWebhookSanitizer.SanitizeWebhookPayload(input);
        Assert.Same(input, result);
    }

    [Fact]
    public void SanitizeWebhookPayload_String_ReturnsSameString()
    {
        var result = BridgeWebhookSanitizer.SanitizeWebhookPayload("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void SanitizeWebhookPayload_Null_ReturnsNull()
    {
        string? input = null;
        var result = BridgeWebhookSanitizer.SanitizeWebhookPayload(input);
        Assert.Null(result);
    }
}
