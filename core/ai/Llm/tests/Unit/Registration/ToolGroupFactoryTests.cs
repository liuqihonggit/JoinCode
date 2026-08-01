namespace Llm.Tests.Registration;

public class ToolGroupFactoryTests
{
    [Fact]
    public void CreateFromObject_ThrowsNotSupportedException()
    {
        var factory = new ToolGroupFactory();

        var act = () => factory.CreateFromObject(new object(), "plugin");

        var ex = act.Should().Throw<NotSupportedException>();
        ex.WithMessage("*CreateFromObject 不再支持反射扫描*");
    }
}
