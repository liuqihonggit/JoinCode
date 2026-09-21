namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneStateStore 单元测试 — AC-05b 跨进程文件持久化
/// </summary>
public sealed class DesktopSceneStateStoreTests {
    /// <summary>AC-05b: 实例 A 写状态 → 实例 B 读状态（模拟跨 mcp_call 进程，共享文件系统）</summary>
    [Fact]
    public async Task SaveThenLoad_PreservesStateAcrossInstances() {
        var storedFiles = new Dictionary<string, string>();
        var fsMock = CreateSharedFileSystemMock(storedFiles);

        await using var storeA = new DesktopSceneStateStore("/fake/scenarios", fsMock.Object);
        var state = new DesktopSceneState("sc_001", 1, "L0.2",
            new[] { new ZoomHistoryEntry(1, "L0.2", 2, DateTimeOffset.UtcNow) },
            null, "zoom", DateTimeOffset.UtcNow);
        await storeA.SaveAsync(state);

        await using var storeB = new DesktopSceneStateStore("/fake/scenarios", fsMock.Object);
        var loaded = await storeB.LoadAsync("sc_001");

        loaded.Should().NotBeNull();
        loaded!.SceneId.Should().Be("sc_001");
        loaded.CurrentDepth.Should().Be(1);
        loaded.CurrentCellCode.Should().Be("L0.2");
        loaded.ZoomHistory.Should().HaveCount(1);
        loaded.ZoomHistory[0].Quadrant.Should().Be(2);
    }

    /// <summary>AC-05b: 不存在的场景返回 null</summary>
    [Fact]
    public async Task Load_NonexistentScene_ReturnsNull() {
        var fsMock = CreateSharedFileSystemMock(new Dictionary<string, string>());
        await using var store = new DesktopSceneStateStore("/fake/scenarios", fsMock.Object);
        var loaded = await store.LoadAsync("sc_nonexistent");
        loaded.Should().BeNull();
    }

    private static Mock<IFileSystem> CreateSharedFileSystemMock(Dictionary<string, string> storedFiles) {
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        fsMock.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns<string>(p => storedFiles.ContainsKey(p));
        fsMock.Setup(f => f.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => storedFiles[path] = content)
            .Returns(Task.CompletedTask);
        fsMock.Setup(f => f.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => Task.FromResult(storedFiles[path]));
        fsMock.Setup(f => f.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));
        return fsMock;
    }
}