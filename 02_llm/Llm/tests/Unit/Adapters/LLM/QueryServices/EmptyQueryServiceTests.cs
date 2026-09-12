namespace Llm.Tests.Adapters.LLM.QueryServices;


public class EmptyQueryServiceTests
{
    [Fact]
    public async Task GetApiMessageContentsAsync_ThrowsNotSupportedException()
    {
        var service = new EmptyQueryService();

        var act = async () => await service.GetApiMessageContentsAsync(new MessageList());

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.WithMessage("*[LLM001]*");
    }

    [Fact]
    public void GetStreamEventContentsAsync_ThrowsNotSupportedException()
    {
        var service = new EmptyQueryService();

        var act = () => service.GetStreamEventContentsAsync(new MessageList());

        var ex = act.Should().Throw<NotSupportedException>();
        ex.WithMessage("*[LLM002]*");
    }
}
