namespace Integration.Tests.PrefixCache.Unit;

public sealed class ImmutablePrefixStableSortTests
{
    [Fact]
    public void Fingerprint_IsStable_RegardlessOfToolRegistrationOrder()
    {
        var toolsAb = new List<ToolSpec>
        {
            new("alpha", "Alpha tool"),
            new("beta", "Beta tool")
        };
        var toolsBa = new List<ToolSpec>
        {
            new("beta", "Beta tool"),
            new("alpha", "Alpha tool")
        };

        var prefixAb = new ImmutablePrefix("System", toolsAb, []);
        var prefixBa = new ImmutablePrefix("System", toolsBa, []);

        prefixAb.Fingerprint.Should().Be(prefixBa.Fingerprint,
            "fingerprint must be identical regardless of tool registration order");
    }

    [Fact]
    public void Fingerprint_IsStable_WithThreeTools_DifferentOrders()
    {
        var tools123 = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files"),
            new("search", "Search code")
        };
        var tools321 = new List<ToolSpec>
        {
            new("search", "Search code"),
            new("write", "Write files"),
            new("read", "Read files")
        };

        var prefix123 = new ImmutablePrefix("System", tools123, []);
        var prefix321 = new ImmutablePrefix("System", tools321, []);

        prefix123.Fingerprint.Should().Be(prefix321.Fingerprint,
            "fingerprint must be order-independent for 3+ tools");
    }

    [Fact]
    public void Fingerprint_DifferentContent_DifferentHash()
    {
        var tools1 = new List<ToolSpec> { new("read", "Read files") };
        var tools2 = new List<ToolSpec> { new("read", "Read files v2") };

        var prefix1 = new ImmutablePrefix("System", tools1, []);
        var prefix2 = new ImmutablePrefix("System", tools2, []);

        prefix1.Fingerprint.Should().NotBe(prefix2.Fingerprint,
            "different tool descriptions must produce different fingerprints");
    }

    [Fact]
    public void Fingerprint_DifferentNames_DifferentHash()
    {
        var tools1 = new List<ToolSpec> { new("read", "Same description") };
        var tools2 = new List<ToolSpec> { new("write", "Same description") };

        var prefix1 = new ImmutablePrefix("System", tools1, []);
        var prefix2 = new ImmutablePrefix("System", tools2, []);

        prefix1.Fingerprint.Should().NotBe(prefix2.Fingerprint,
            "different tool names must produce different fingerprints");
    }

    [Fact]
    public void Fingerprint_WithInputSchema_StableAcrossOrder()
    {
        var toolsAb = new List<ToolSpec>
        {
            new("alpha", "Alpha tool", """{"type":"object","properties":{"path":{"type":"string"}}}"""),
            new("beta", "Beta tool", """{"type":"object","properties":{"query":{"type":"string"}}}""")
        };
        var toolsBa = new List<ToolSpec>
        {
            new("beta", "Beta tool", """{"type":"object","properties":{"query":{"type":"string"}}}"""),
            new("alpha", "Alpha tool", """{"type":"object","properties":{"path":{"type":"string"}}}""")
        };

        var prefixAb = new ImmutablePrefix("System", toolsAb, []);
        var prefixBa = new ImmutablePrefix("System", toolsBa, []);

        prefixAb.Fingerprint.Should().Be(prefixBa.Fingerprint,
            "fingerprint must be order-independent even with InputSchemaJson");
    }

    [Fact]
    public void AddTool_InvalidatesFingerprintCache()
    {
        var prefix = new ImmutablePrefix("System", [new ToolSpec("alpha", "Alpha")], []);
        var fp1 = prefix.Fingerprint;

        prefix.AddTool(new ToolSpec("beta", "Beta"));
        var fp2 = prefix.Fingerprint;

        fp2.Should().NotBe(fp1, "AddTool must invalidate fingerprint cache");
    }

    [Fact]
    public void RemoveTool_InvalidatesFingerprintCache()
    {
        var prefix = new ImmutablePrefix("System",
            [new ToolSpec("alpha", "Alpha"), new ToolSpec("beta", "Beta")], []);
        var fp1 = prefix.Fingerprint;

        prefix.RemoveTool("alpha");
        var fp2 = prefix.Fingerprint;

        fp2.Should().NotBe(fp1, "RemoveTool must invalidate fingerprint cache");
    }

    [Fact]
    public void Fingerprint_SameTools_SameSystem_SameResult()
    {
        var tools = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files")
        };

        var prefix1 = new ImmutablePrefix("System prompt", tools, []);
        var prefix2 = new ImmutablePrefix("System prompt", tools, []);

        prefix1.Fingerprint.Should().Be(prefix2.Fingerprint,
            "identical inputs must produce identical fingerprints");
    }
}
