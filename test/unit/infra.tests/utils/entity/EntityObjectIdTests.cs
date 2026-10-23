namespace Infra.Tests.EntityTests;

public sealed class EntityObjectIdTests {
    [Fact]
    public void ObjectId_SequenceId_ShouldBeAtomicIncrementPerType() {
        var id1 = new ObjectId(ObjectType.Agent);
        var id2 = new ObjectId(ObjectType.Agent);
        var id3 = new ObjectId(ObjectType.Agent);

        id2.SequenceId.Should().BeGreaterThan(id1.SequenceId);
        id3.SequenceId.Should().BeGreaterThan(id2.SequenceId);
    }

    [Fact]
    public void ObjectId_UniqueId_ShouldBeGuidFormat() {
        var id = new ObjectId(ObjectType.Agent);
        id.UniqueId.Should().StartWith("agent-");
        id.UniqueId.Length.Should().BeGreaterThan(8);
    }

    [Fact]
    public void ObjectId_DisplayName_DefaultsToUniqueId() {
        var id = new ObjectId(ObjectType.Agent);
        id.DisplayName.Should().Be(id.UniqueId);
    }

    [Fact]
    public void ObjectId_DisplayName_Custom() {
        var id = new ObjectId(ObjectType.Agent, "my-agent");
        id.DisplayName.Should().Be("my-agent");
    }

    [Fact]
    public void ObjectId_ToString_Format() {
        var id = new ObjectId(ObjectType.Agent);
        id.ToString().Should().StartWith("Agent:");
    }

    [Fact]
    public void ObjectId_DifferentSequenceId_NotEqual() {
        var id1 = new ObjectId(ObjectType.Agent);
        var id2 = new ObjectId(ObjectType.Agent);
        id1.Should().NotBe(id2);
    }
}