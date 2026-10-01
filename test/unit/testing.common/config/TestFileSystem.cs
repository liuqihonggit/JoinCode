namespace Testing.Common;

/// <summary>
/// 测试文件系统切换中心 — 一键切换所有测试的虚拟/真实文件系统
/// <para>设置 UseRealFileSystem = true 切换到真实磁盘（PhysicalFileSystem）</para>
/// <para>默认 UseRealFileSystem = false 使用内存文件系统（InMemoryFileSystem，0磁盘IO）</para>
/// </summary>
public static class TestFileSystem {
    private static readonly IFileSystem _inMemoryInstance = new IO.FileSystem.InMemoryFileSystem();
    private static readonly IFileSystem _physicalInstance = new IO.FileSystem.PhysicalFileSystem();

    /// <summary>
    /// 全局切换: true = PhysicalFileSystem (真实磁盘), false = InMemoryFileSystem (内存)
    /// 可在 AssemblyInitialize 或任意测试前设置
    /// </summary>
    public static bool UseRealFileSystem { get; set; } = false;

    /// <summary>
    /// 获取当前配置的文件系统实例（单例，保证同一测试内多个对象共享同一文件系统视图）
    /// </summary>
    public static IFileSystem Current => UseRealFileSystem
        ? _physicalInstance
        : _inMemoryInstance;
}