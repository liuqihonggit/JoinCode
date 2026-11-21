namespace Guard.Tests.Security.Sandbox;

/// <summary>
/// SandboxManagerCapabilityExtensions 测试 — 「真隔离」口径必须同时具备文件系统与网络隔离，
/// 只有路径重定向/进程隔离的 Soft/Process 提供器不算。
/// </summary>
public class SandboxManagerCapabilityExtensionsTest {
    private static readonly ISandboxProvider RealProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == (SandboxCapabilities.FileSystemIsolation
                                 | SandboxCapabilities.NetworkIsolation
                                 | SandboxCapabilities.ProcessIsolation));

    private static readonly ISandboxProvider PathOnlyProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == (SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation));

    private static ISandboxManager CreateManager(SandboxType type = SandboxType.Bubblewrap) {
        var manager = new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns([type]);
        return manager.Object;
    }

    [Fact]
    public void Given_能力位仅路径与文件_When_HasRealIsolation_Then_false() {
        (SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation).HasRealIsolation()
            .Should().BeFalse("缺少网络隔离不构成真隔离");
    }

    [Fact]
    public void Given_能力位含文件与网络_When_HasRealIsolation_Then_true() {
        (SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.NetworkIsolation).HasRealIsolation()
            .Should().BeTrue();
    }

    [Fact]
    public void Given_管理器为空_When_HasRealIsolationProvider_Then_false() {
        ((ISandboxManager?)null).HasRealIsolationProvider().Should().BeFalse();
    }

    [Fact]
    public void Given_提供器为真隔离_When_HasRealIsolationProvider_Then_true() {
        var manager = new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns([SandboxType.Bubblewrap]);
        manager.Setup(m => m.GetProvider(SandboxType.Bubblewrap)).Returns(RealProvider);

        manager.Object.HasRealIsolationProvider().Should().BeTrue();
    }

    [Fact]
    public void Given_提供器只有路径隔离_When_HasRealIsolationProvider_Then_false() {
        var manager = new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns([SandboxType.Process]);
        manager.Setup(m => m.GetProvider(SandboxType.Process)).Returns(PathOnlyProvider);

        manager.Object.HasRealIsolationProvider().Should().BeFalse();
    }

    [Fact]
    public void Given_提供器查不到_When_HasRealIsolationProvider_Then_false() {
        CreateManager(SandboxType.Docker).HasRealIsolationProvider()
            .Should().BeFalse("GetProvider 未返回实例时不能假设可用");
    }

    [Fact]
    public void Given_无可用类型_When_HasRealIsolationProvider_Then_false() {
        var manager = new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns(Array.Empty<SandboxType>());

        manager.Object.HasRealIsolationProvider().Should().BeFalse();
    }
}
