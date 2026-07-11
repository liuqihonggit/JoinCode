namespace IO.FileSystem;

/// <summary>
/// 文件系统工厂 — 根据 JCC_FILE_SYSTEM_MODE 环境变量创建对应的 IFileSystem 实例
/// 用于 DI 容器构建之前（如 Program.Main）的场景，DI 容器构建后应通过注入 IFileSystem 使用
/// </summary>
public static class FileSystemFactory
{
    /// <summary>
    /// 根据环境变量创建 IFileSystem 实例。
    /// JCC_FILE_SYSTEM_MODE=InMemory → InMemoryFileSystem（纯内存，0磁盘IO）
    /// 其他/未设置 → PhysicalFileSystem（真实磁盘，默认）
    /// </summary>
    public static IFileSystem Create()
    {
        var fsMode = EnvHelper.Get(JccEnvVar.FileSystemMode);
        if (string.Equals(fsMode, "InMemory", StringComparison.OrdinalIgnoreCase))
            return new InMemoryFileSystem();
        return new PhysicalFileSystem();
    }
}
