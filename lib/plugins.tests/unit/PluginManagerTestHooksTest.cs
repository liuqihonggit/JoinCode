// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginManager internal 测试钩子单元测试 — 仅验证黑名单和资源 ObjectId 记录的 internal 钩子
/// <para>确定性测试:不涉及加载/卸载全流程,仅测纯字段操作的 internal 方法</para>
/// </summary>
public sealed class PluginManagerTestHooksTest {
    private static PluginManager CreateManager() => new(new InMemoryFileSystem());

    [Fact]
    public async Task AddToBlacklistForTest_ThenIsBlacklistedForTest_ReturnsTrue() {
        await using var manager = CreateManager();

        manager.AddToBlacklistForTest("bad-plugin");

        manager.IsBlacklistedForTest("bad-plugin").Should().BeTrue();
    }

    [Fact]
    public async Task IsBlacklistedForTest_NotInBlacklist_ReturnsFalse() {
        await using var manager = CreateManager();

        manager.IsBlacklistedForTest("nonexistent").Should().BeFalse();
    }

    [Fact]
    public async Task AddToBlacklistForTest_DoesNotAffectOtherPlugins() {
        await using var manager = CreateManager();
        manager.AddToBlacklistForTest("pluginA");

        manager.IsBlacklistedForTest("pluginB").Should().BeFalse();
    }

    [Fact]
    public async Task RecordPluginResourceIds_SingleType_AggregatesIntoLongRangeSet() {
        await using var manager = CreateManager();
        var id1 = new ObjectId(ObjectType.Resource);
        var id2 = new ObjectId(ObjectType.Resource);
        var id3 = new ObjectId(ObjectType.Resource);

        manager.RecordPluginResourceIds("p", [id1, id2, id3]);

        var ranges = manager.GetPluginResourceIdsForTest("p", ObjectType.Resource);
        ranges.Count.Should().Be(3);
        ranges.Contains(id1.SequenceId).Should().BeTrue();
        ranges.Contains(id2.SequenceId).Should().BeTrue();
        ranges.Contains(id3.SequenceId).Should().BeTrue();
    }

    [Fact]
    public async Task RecordPluginResourceIds_MultipleTypes_GroupsByObjectType() {
        await using var manager = CreateManager();
        var resourceId = new ObjectId(ObjectType.Resource);
        var pluginId = new ObjectId(ObjectType.Plugin);

        manager.RecordPluginResourceIds("p", [resourceId, pluginId]);

        manager.GetPluginResourceIdsForTest("p", ObjectType.Resource).Count.Should().Be(1);
        manager.GetPluginResourceIdsForTest("p", ObjectType.Plugin).Count.Should().Be(1);
    }

    [Fact]
    public async Task RecordPluginResourceIds_OverwritesPreviousRecord() {
        await using var manager = CreateManager();
        var id1 = new ObjectId(ObjectType.Resource);
        manager.RecordPluginResourceIds("p", [id1]);

        var id2 = new ObjectId(ObjectType.Resource);
        manager.RecordPluginResourceIds("p", [id2]);

        var ranges = manager.GetPluginResourceIdsForTest("p", ObjectType.Resource);
        ranges.Count.Should().Be(1);
        ranges.Contains(id2.SequenceId).Should().BeTrue();
    }

    [Fact]
    public async Task GetPluginResourceIdsForTest_NotRecorded_ReturnsEmpty() {
        await using var manager = CreateManager();

        var ranges = manager.GetPluginResourceIdsForTest("nonexistent", ObjectType.Resource);
        ranges.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task RecordPluginResourceIds_ContinuousIds_CompressesIntoSingleRange() {
        await using var manager = CreateManager();
        // 连续的 SequenceId 应压缩为单区间
        var ids = new List<ObjectId>();
        for (var i = 0; i < 10; i++)
            ids.Add(new ObjectId(ObjectType.Resource));

        manager.RecordPluginResourceIds("p", ids);

        var ranges = manager.GetPluginResourceIdsForTest("p", ObjectType.Resource);
        ranges.Count.Should().Be(10);
        // 连续区间应压缩为 RangeCount=1
        ranges.RangeCount.Should().Be(1);
    }

    // ===== null 参数守卫 =====

    [Fact]
    public async Task RecordPluginResourceIds_NullPluginName_ThrowsArgumentNullException() {
        await using var manager = CreateManager();

        Action act = () => manager.RecordPluginResourceIds(null!, []);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("pluginName");
    }

    [Fact]
    public async Task RecordPluginResourceIds_EmptyPluginName_ThrowsArgumentException() {
        await using var manager = CreateManager();

        Action act = () => manager.RecordPluginResourceIds("", []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RecordPluginResourceIds_WhiteSpacePluginName_ThrowsArgumentException() {
        await using var manager = CreateManager();

        Action act = () => manager.RecordPluginResourceIds("   ", []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RecordPluginResourceIds_NullResourceIds_ThrowsArgumentNullException() {
        await using var manager = CreateManager();

        Action act = () => manager.RecordPluginResourceIds("p", null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resourceIds");
    }
}
