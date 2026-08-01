namespace Llm.Tests.Adapters;

public sealed class ChatClientTests
{
    [Fact]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        var act = () => new ChatClient(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("chatCompletionService");
    }

    [Fact]
    public void Constructor_ExposesCompletionService()
    {
        var queryService = new Mock<IQueryService>().Object;
        var client = new ChatClient(queryService);

        client.GetChatCompletionService().Should().Be(queryService);
    }

    [Fact]
    public void Plugins_ReturnsEmptyCollectionByDefault()
    {
        var client = new ChatClient(new Mock<IQueryService>().Object);

        client.Plugins.PluginNames.Should().BeEmpty();
    }

    [Fact]
    public void ToolCollection_Add_Get_Remove()
    {
        var collection = new ToolCollection();
        var group = new ToolGroup("test", []);

        collection.GetPlugin("test").Should().BeNull();

        collection.Add(group);
        collection.PluginNames.Should().ContainSingle("test");
        collection.GetPlugin("test").Should().BeSameAs(group);

        collection.Remove("test").Should().BeTrue();
        collection.Remove("test").Should().BeFalse();
        collection.GetPlugin("test").Should().BeNull();
    }

    [Fact]
    public void ToolCollection_Add_OverwritesExistingGroup()
    {
        var collection = new ToolCollection();
        var first = new ToolGroup("test", []);
        var second = new ToolGroup("test", []);

        collection.Add(first);
        collection.Add(second);

        collection.GetPlugin("test").Should().BeSameAs(second);
    }

    [Fact]
    public void ToolCollection_PluginNames_IsCaseInsensitive()
    {
        var collection = new ToolCollection();
        collection.Add(new ToolGroup("Test", []));

        collection.GetPlugin("test").Should().NotBeNull();
        collection.GetPlugin("TEST").Should().NotBeNull();
    }

    [Fact]
    public void ToolGroup_StoresNameAndFunctions()
    {
        var functions = new List<IToolDef>
        {
            new ToolDef("read", "Reads a file")
        };

        var group = new ToolGroup("files", functions);

        group.Name.Should().Be("files");
        group.Functions.Should().HaveCount(1);
        group.Functions[0].Name.Should().Be("read");
    }

    [Fact]
    public void ToolDef_StoresProperties()
    {
        var parameters = new List<IToolParam>
        {
            new ToolParam("path", "file path", typeof(string), true)
        };

        var def = new ToolDef("read", "Reads a file", parameters);

        def.Name.Should().Be("read");
        def.Description.Should().Be("Reads a file");
        def.Parameters.Should().HaveCount(1);
        def.Parameters[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public void ToolDef_DefaultParameters_Empty()
    {
        var def = new ToolDef("read", "Reads a file");

        def.Parameters.Should().NotBeNull();
        def.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ToolParam_StoresProperties()
    {
        var param = new ToolParam("count", "number of items", typeof(int), true);

        param.Name.Should().Be("count");
        param.Description.Should().Be("number of items");
        param.ParameterType.Should().Be(typeof(int));
        param.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void ToolParam_DefaultValues_AreExpected()
    {
        var param = new ToolParam("x");

        param.Description.Should().BeEmpty();
        param.ParameterType.Should().BeNull();
        param.IsRequired.Should().BeFalse();
    }
}
