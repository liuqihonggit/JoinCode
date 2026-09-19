namespace Core.Tests;

public class SandboxGuardNodeTests {
    [Fact]
    public async Task ResolvePathAsync_NullSandbox_ReturnsOriginalPath() {
        var node = new SandboxGuardNode();

        var result = await node.ResolvePathAsync("/test/path.txt", CancellationToken.None);

        Assert.Equal("/test/path.txt", result);
    }

    [Fact]
    public async Task ResolvePathAsync_SandboxNotActive_ReturnsOriginalPath() {
        var sandbox = new Mock<ISandboxManager>();
        sandbox.SetupGet(s => s.IsInSandbox).Returns(false);
        var node = new SandboxGuardNode(sandbox.Object);

        var result = await node.ResolvePathAsync("/test/path.txt", CancellationToken.None);

        Assert.Equal("/test/path.txt", result);
    }
}