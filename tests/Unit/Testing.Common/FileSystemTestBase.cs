namespace Testing.Common;

/// <summary>
/// 文件系统测试基类 — 提供一个变量切换真实/虚拟文件系统
/// 用法: 测试类继承此类，通过 FileSystem 属性获取 IFileSystem 实例
/// 切换方式: 修改 UseRealFileSystem 静态属性即可全局切换
/// </summary>
public abstract class FileSystemTestBase
{
    /// <summary>
    /// 全局切换: true = PhysicalFileSystem (真实磁盘), false = InMemoryFileSystem (内存)
    /// 默认 false，即使用内存文件系统实现0磁盘读写
    /// </summary>
    public static bool UseRealFileSystem { get; set; } = false;

    /// <summary>
    /// 当前测试使用的文件系统实例
    /// 使用 _inMemoryFs 保证与 InMemoryFs 属性返回同一实例，避免测试预设和生产代码操作不同实例
    /// </summary>
    protected IFileSystem GetFileSystem() => UseRealFileSystem
        ? new IO.FileSystem.PhysicalFileSystem()
        : _inMemoryFs;

    /// <summary>
    /// 内存文件系统实例（仅在 UseRealFileSystem=false 时使用）
    /// 暴露出来以便测试中可以预设文件/目录
    /// </summary>
    protected IO.FileSystem.InMemoryFileSystem InMemoryFs => _inMemoryFs;

    private readonly IO.FileSystem.InMemoryFileSystem _inMemoryFs = new();
}
