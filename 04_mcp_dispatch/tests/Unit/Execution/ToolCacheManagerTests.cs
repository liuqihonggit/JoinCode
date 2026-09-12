namespace McpToolRegistry.Tests;

public class ToolCacheKeysTests
{
    [Fact]
    public void ForTool_ReturnsCorrectKey()
    {
        ToolCacheKeys.ForTool("bash").Should().Be("toolinfo:bash");
    }

    [Fact]
    public void ForTool_DifferentNames_ReturnDifferentKeys()
    {
        ToolCacheKeys.ForTool("bash").Should().NotBe(ToolCacheKeys.ForTool("read"));
    }

    [Fact]
    public void AllTools_ReturnsCorrectKey()
    {
        ToolCacheKeys.AllTools.Should().Be("toolinfo:all");
    }

    [Fact]
    public void ForClientPrefix_ReturnsCorrectPrefix()
    {
        ToolCacheKeys.ForClientPrefix("mcp1").Should().Be("mcp1.");
    }

    [Fact]
    public void ForClientPrefix_DifferentClients_ReturnDifferentPrefixes()
    {
        ToolCacheKeys.ForClientPrefix("mcp1").Should().NotBe(ToolCacheKeys.ForClientPrefix("mcp2"));
    }
}

public class ToolCacheManagerTests
{
    private readonly IMemoryCache _cache;
    private readonly WorkflowConfig _config;
    private readonly ToolCacheManager _manager;

    public ToolCacheManagerTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _config = CreateTestConfig();
        _manager = new ToolCacheManager(_cache, _config);
    }

    private static WorkflowConfig CreateTestConfig()
    {
        var config = new WorkflowConfig();
        config.ToolExecution.ToolCacheExpirationMinutes = 30;
        return config;
    }

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new ToolCacheManager(null!, _config);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        var act = () => new ToolCacheManager(_cache, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetToolInfo_CacheMiss_CallsFactory()
    {
        var toolInfo = new ToolInfo { Name = "bash", Description = "Run bash" };
        var factoryCalled = false;

        var result = _manager.GetToolInfo("bash", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeTrue();
        result.Should().Be(toolInfo);
    }

    [Fact]
    public void GetToolInfo_CacheHit_ReturnsCachedValue()
    {
        var toolInfo = new ToolInfo { Name = "bash", Description = "Run bash" };
        _manager.GetToolInfo("bash", () => toolInfo);

        var factoryCalled = false;
        var result = _manager.GetToolInfo("bash", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeFalse();
        result.Should().Be(toolInfo);
    }

    [Fact]
    public void GetToolInfo_FactoryReturnsNull_ReturnsNull()
    {
        var result = _manager.GetToolInfo("unknown", () => null);
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllToolInfos_CacheMiss_CallsFactory()
    {
        var tools = new List<ToolInfo>
        {
            new ToolInfo { Name = "bash", Description = "Run bash" },
            new ToolInfo { Name = "read", Description = "Read file" }
        };

        var factoryCalled = false;
        var result = _manager.GetAllToolInfos(() =>
        {
            factoryCalled = true;
            return tools;
        });

        factoryCalled.Should().BeTrue();
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetAllToolInfos_CacheHit_ReturnsCachedValue()
    {
        var tools = new List<ToolInfo>
        {
            new ToolInfo { Name = "bash", Description = "Run bash" }
        };

        _manager.GetAllToolInfos(() => tools);

        var factoryCalled = false;
        var result = _manager.GetAllToolInfos(() =>
        {
            factoryCalled = true;
            return tools;
        });

        factoryCalled.Should().BeFalse();
        result.Should().HaveCount(1);
    }

    [Fact]
    public void InvalidateToolCache_RemovesSpecificTool()
    {
        var toolInfo = new ToolInfo { Name = "bash", Description = "Run bash" };
        _manager.GetToolInfo("bash", () => toolInfo);

        _manager.InvalidateToolCache("bash");

        var factoryCalled = false;
        _manager.GetToolInfo("bash", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void InvalidateToolCache_AlsoInvalidatesAllToolsCache()
    {
        var tools = new List<ToolInfo> { new() { Name = "bash", Description = "Run bash" } };
        _manager.GetAllToolInfos(() => tools);

        _manager.InvalidateToolCache("bash");

        var factoryCalled = false;
        _manager.GetAllToolInfos(() =>
        {
            factoryCalled = true;
            return tools;
        });

        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void InvalidateAllCache_ClearsEverything()
    {
        var toolInfo = new ToolInfo { Name = "bash", Description = "Run bash" };
        _manager.GetToolInfo("bash", () => toolInfo);

        _manager.InvalidateAllCache();

        var factoryCalled = false;
        _manager.GetToolInfo("bash", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void InvalidateClientTools_InvalidatesMatchingTools()
    {
        var toolInfo = new ToolInfo { Name = "mcp1.tool1", Description = "Tool 1" };
        _manager.GetToolInfo("mcp1.tool1", () => toolInfo);

        _manager.InvalidateClientTools("mcp1", () => ["mcp1.tool1", "mcp2.tool2"]);

        var factoryCalled = false;
        _manager.GetToolInfo("mcp1.tool1", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void InvalidateClientTools_DoesNotInvalidateOtherClientTools()
    {
        var toolInfo = new ToolInfo { Name = "mcp2.tool1", Description = "Tool 1" };
        _manager.GetToolInfo("mcp2.tool1", () => toolInfo);

        _manager.InvalidateClientTools("mcp1", () => ["mcp1.tool1", "mcp2.tool1"]);

        var factoryCalled = false;
        _manager.GetToolInfo("mcp2.tool1", () =>
        {
            factoryCalled = true;
            return toolInfo;
        });

        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void CacheExpiration_ReturnsConfiguredValue()
    {
        _manager.CacheExpiration.Should().Be(TimeSpan.FromMinutes(30));
    }
}
