namespace Abs.Tests.LLM.Chat;


public class MessageListFromListTests
{
    [Fact]
    public void FromList_Preserves_Element_Count_And_References()
    {
        var msg1 = new ApiMessage(MessageRole.System, "system");
        var msg2 = new ApiMessage(MessageRole.User, "hello");
        var source = new List<ApiMessage> { msg1, msg2 };

        var ml = MessageList.FromList(source);

        ml.Count.Should().Be(2);
        ml[0].Should().BeSameAs(msg1);
        ml[1].Should().BeSameAs(msg2);
    }

    [Fact]
    public void FromList_Empty_List_Produces_Empty_MessageList()
    {
        var source = new List<ApiMessage>();

        var ml = MessageList.FromList(source);

        ml.Count.Should().Be(0);
    }

    [Fact]
    public void FromList_Single_Element_Roundtrips()
    {
        var msg = new ApiMessage(MessageRole.Assistant, "response");
        var source = new List<ApiMessage> { msg };

        var ml = MessageList.FromList(source);

        ml.Count.Should().Be(1);
        ml[0].Role.Should().Be(MessageRole.Assistant);
        ml[0].Content.Should().Be("response");
    }

    [Fact]
    public void FromList_Supports_Enumeration()
    {
        var msgs = new List<ApiMessage>
        {
            new(MessageRole.System, "s"),
            new(MessageRole.User, "u1"),
            new(MessageRole.User, "u2"),
        };

        var ml = MessageList.FromList(msgs);

        var roles = ml.Select(m => m.Role).ToArray();
        roles.Should().Equal(MessageRole.System, MessageRole.User, MessageRole.User);
    }

    [Fact]
    public void FromList_Add_After_Creation_Works()
    {
        var source = new List<ApiMessage>
        {
            new(MessageRole.User, "first"),
        };

        var ml = MessageList.FromList(source);
        ml.Add(new ApiMessage(MessageRole.Assistant, "second"));

        ml.Count.Should().Be(2);
        ml[1].Content.Should().Be("second");
    }
}
