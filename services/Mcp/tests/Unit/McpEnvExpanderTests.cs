namespace Mcp.Tests;

public sealed class McpEnvExpanderTests
{
    [Fact]
    public void ExpandEnvVarsInString_NoVars_ReturnsOriginal()
    {
        var (expanded, missing) = McpEnvExpander.ExpandEnvVarsInString("no-vars-here");
        expanded.Should().Be("no-vars-here");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void ExpandEnvVarsInString_ExistingVar_Expands()
    {
        Environment.SetEnvironmentVariable("JCC_TEST_STRYKER", "expanded_value");
        try
        {
            var (expanded, missing) = McpEnvExpander.ExpandEnvVarsInString("${JCC_TEST_STRYKER}");
            expanded.Should().Be("expanded_value");
            missing.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_TEST_STRYKER", null);
        }
    }

    [Fact]
    public void ExpandEnvVarsInString_MissingVar_ReportsMissing()
    {
        Environment.SetEnvironmentVariable("JCC_MISSING_VAR_XYZ", null);
        var (expanded, missing) = McpEnvExpander.ExpandEnvVarsInString("${JCC_MISSING_VAR_XYZ}");
        missing.Should().Contain("JCC_MISSING_VAR_XYZ");
    }

    [Fact]
    public void ExpandEnvVarsInString_DefaultValue_UsedWhenMissing()
    {
        Environment.SetEnvironmentVariable("JCC_DEFAULT_VAR_XYZ", null);
        var (expanded, missing) = McpEnvExpander.ExpandEnvVarsInString("${JCC_DEFAULT_VAR_XYZ:-fallback}");
        expanded.Should().Be("fallback");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void ExpandEnvVarsInString_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => McpEnvExpander.ExpandEnvVarsInString(null!));
    }

    [Fact]
    public void ExpandEnvironmentValues_NullInput_ReturnsEmpty()
    {
        var result = McpEnvExpander.ExpandEnvironmentValues(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExpandEnvironmentValues_NoDollar_ReturnsAsIs()
    {
        var input = new Dictionary<string, string> { ["key"] = "value" };
        var result = McpEnvExpander.ExpandEnvironmentValues(input);
        result["key"].Should().Be("value");
    }

    [Fact]
    public void ExpandEndpoint_NoDollar_ReturnsAsIs()
    {
        McpEnvExpander.ExpandEndpoint("http://localhost:8080").Should().Be("http://localhost:8080");
    }

    [Fact]
    public void ExpandEndpoint_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => McpEnvExpander.ExpandEndpoint(null!));
    }
}
