namespace Testing.Common;

/// <summary>
/// 测试文件系统切换中心 — 一键切换所有测试的虚拟/真实文件系统
/// <para>设置 UseRealFileSystem = true 切换到真实磁盘（PhysicalFileSystem）</para>
/// <para>默认 UseRealFileSystem = false 使用内存文件系统（InMemoryFileSystem，0磁盘IO）</para>
/// </summary>
public static class TestFileSystem
{
    /// <summary>
    /// 全局切换: true = PhysicalFileSystem (真实磁盘), false = InMemoryFileSystem (内存)
    /// 可在 AssemblyInitialize 或任意测试前设置
    /// </summary>
    public static bool UseRealFileSystem { get; set; } = false;

    /// <summary>
    /// 获取当前配置的文件系统实例（每次调用创建新实例，保证测试隔离）
    /// </summary>
    public static IFileSystem Current => UseRealFileSystem
        ? new IO.FileSystem.PhysicalFileSystem()
        : new IO.FileSystem.InMemoryFileSystem();
}
