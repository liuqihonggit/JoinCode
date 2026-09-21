namespace Core.Tests.Plugins;

public sealed class ObjectTypeAndObjectIdManagerTests {
    [Fact]
    public void ObjectType_Plugin_Exists() {
        ((int)ObjectType.Plugin).Should().Be(20);
        ObjectTypeEnumConstants.Plugin.Should().Be("plugin");
    }

    [Fact]
    public void ObjectType_Resource_Exists() {
        ((int)ObjectType.Resource).Should().Be(21);
        ObjectTypeEnumConstants.Resource.Should().Be("resource");
    }

    [Fact]
    public async Task ObjectIdManager_IsRegistered_ReturnsTrueForRegistered() {
        ObjectIdManager.Clear();
        await using var entity = new TestEntity(ObjectType.Plugin, "test-plugin");

        ObjectIdManager.IsRegistered(entity.ObjectId).Should().BeTrue();
        await entity.DisposeAsync().ConfigureAwait(false);
        ObjectIdManager.IsRegistered(entity.ObjectId).Should().BeFalse();
    }

    [Fact]
    public void ObjectIdManager_IsRegistered_ReturnsFalseForUnregistered() {
        ObjectIdManager.Clear();
        var id = new ObjectId(ObjectType.Plugin, "nonexistent");

        ObjectIdManager.IsRegistered(id).Should().BeFalse();
    }

    private sealed class TestEntity : Entity {
        public readonly ObjectId objectId;
        public TestEntity(ObjectType type, string displayName) : base(type, displayName: displayName) {
            objectId = ObjectId;
        }
        public override void Dispose() => base.Dispose();
    }
}