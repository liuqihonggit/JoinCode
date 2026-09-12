namespace Llm.Tests.Adapters.LLM;

public class ChatClientTests
{
    [Fact]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        var act = () => new ChatClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetChatCompletionService_ReturnsInjectedService()
    {
        var mockService = new Mock<IQueryService>();
        var client = new ChatClient(mockService.Object);

        client.GetChatCompletionService().Should().BeSameAs(mockService.Object);
    }

    [Fact]
    public void Plugins_IsNotNull()
    {
        var client = new ChatClient(new Mock<IQueryService>().Object);
        client.Plugins.Should().NotBeNull();
    }

    [Fact]
    public void ToolCollection_AddAndGetPlugin_ReturnsPlugin()
    {
        var client = new ChatClient(new Mock<IQueryService>().Object);
        var plugin = new ToolGroup("test", [new ToolDef("fn", "desc")]);

        client.Plugins.Add(plugin);

        client.Plugins.GetPlugin("test").Should().BeSameAs(plugin);
        client.Plugins.PluginNames.Should().Contain("test");
    }

    [Fact]
    public void ToolCollection_Remove_RemovesPlugin()
    {
        var client = new ChatClient(new Mock<IQueryService>().Object);
        client.Plugins.Add(new ToolGroup("test", []));

        var removed = client.Plugins.Remove("test");

        removed.Should().BeTrue();
        client.Plugins.GetPlugin("test").Should().BeNull();
    }

    [Fact]
    public void ToolCollection_GetMissingPlugin_ReturnsNull()
    {
        var client = new ChatClient(new Mock<IQueryService>().Object);
        client.Plugins.GetPlugin("missing").Should().BeNull();
    }

    [Fact]
    public void ToolGroup_Properties_ExposeNameAndFunctions()
    {
        var functions = new[] { new ToolDef("a", "desc a"), new ToolDef("b", "desc b") };
        var group = new ToolGroup("group", functions);

        group.Name.Should().Be("group");
        group.Functions.Should().HaveCount(2);
    }

    [Fact]
    public void ToolDef_Properties_ExposeValues()
    {
        var parameters = new[] { new ToolParam("p", "param", typeof(int), true) };
        var def = new ToolDef("name", "description", parameters);

        def.Name.Should().Be("name");
        def.Description.Should().Be("description");
        def.Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void ToolDef_DefaultParameters_IsEmpty()
    {
        var def = new ToolDef("name", "description");
        def.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ToolParam_Properties_ExposeValues()
    {
        var param = new ToolParam("p", "param", typeof(bool), true);

        param.Name.Should().Be("p");
        param.Description.Should().Be("param");
        param.ParameterType.Should().Be(typeof(bool));
        param.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void ToolParam_DefaultValues_AreSensible()
    {
        var param = new ToolParam("p");

        param.Description.Should().BeEmpty();
        param.ParameterType.Should().BeNull();
        param.IsRequired.Should().BeFalse();
    }
}
