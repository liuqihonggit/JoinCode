namespace Llm.Tests.Adapters.QueryServices;

public sealed class EmptyQueryServiceTests
{
    [Fact]
    public async Task GetApiMessageContentsAsync_ThrowsNotSupportedException()
    {
        var service = new EmptyQueryService();

        var act = () => service.GetApiMessageContentsAsync(new MessageList());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_ThrowsNotSupportedException()
    {
        var service = new EmptyQueryService();

        var act = async () =>
        {
            await foreach (var _ in service.GetStreamEventContentsAsync(new MessageList()))
            {
            }
        };

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
