namespace Llm.Tests.Adapters;

public sealed class ToolGroupFactoryTests {
    [Fact]
    public void CreateFromObject_ThrowsNotSupportedException() {
        await using var factory = new ToolGroupFactory();

        var act = () => factory.CreateFromObject(new object(), "plugin");

        act.Should().Throw<NotSupportedException>();
    }
}