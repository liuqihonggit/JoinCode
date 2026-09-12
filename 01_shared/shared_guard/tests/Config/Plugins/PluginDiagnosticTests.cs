namespace Core.Tests.Plugins;

public sealed class PluginDiagnosticTests
{
    [Fact]
    public void ToString_ContainsAllFields()
    {
        var d = new PluginDiagnostic
        {
            PluginId = "test-plugin",
            Kind = PluginDiagnosticKind.RevertFailed,
            Message = "撤销失败",
            Timestamp = new DateTimeOffset(2026, 9, 10, 14, 30, 0, TimeSpan.Zero),
        };
        var s = d.ToString();
        Assert.Contains("[RevertFailed]", s);
        Assert.Contains("[test-plugin]", s);
        Assert.Contains("撤销失败", s);
    }

    [Fact]
    public void ToString_WithSuggestion_ContainsSuggestion()
    {
        var d = new PluginDiagnostic
        {
            PluginId = "p",
            Kind = PluginDiagnosticKind.AlcLeak,
            Message = "ALC 泄漏",
            Suggestion = "检查跨插件强引用",
        };
        var s = d.ToString();
        Assert.Contains("建议:", s);
        Assert.Contains("检查跨插件强引用", s);
    }

    [Fact]
    public void ToString_WithoutSuggestion_NoSuggestionLine()
    {
        var d = new PluginDiagnostic
        {
            PluginId = "p",
            Kind = PluginDiagnosticKind.ActivationFailed,
            Message = "激活失败",
        };
        var s = d.ToString();
        Assert.DoesNotContain("建议:", s);
    }

    [Fact]
    public void Timestamp_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var d = new PluginDiagnostic { PluginId = "p", Kind = PluginDiagnosticKind.EmptyRegistration, Message = "m" };
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(d.Timestamp, before, after);
    }
}
