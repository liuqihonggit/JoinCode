namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引排除目录目录 — 强制跳过的目录名唯一数据源
/// 消除 FileWatcherIntegration/IncrementalUpdater/CodeIndexer 三处硬编码 { "bin", "obj", ".git", ".x" } 的重复定义。
///
/// 委托 ExcludedDirectoryCatalog.CodeIndexExcluded(单数据源统一):
/// 本类保留为兼容入口,避免破坏现有引用(CodeIndexer/IncrementalUpdater/FileWatcherIntegration),
/// 实际数据源已下沉到 ExcludedDirectoryCatalog(消除跨模块重复定义)。
///
/// 排除原因:
/// - bin/obj: .NET 编译产物,扫描无意义且体积大
/// - .git: Git 内部目录,扫描无意义
/// - .x: 临时归档目录(见 AGENTS.md .xxx/ 归档规则)
/// </summary>
public static class CodeIndexExcludedDirCatalog {
    /// <summary>
    /// 强制排除的目录名集合（OrdinalIgnoreCase）
    /// 委托 ExcludedDirectoryCatalog.CodeIndexExcluded — 唯一数据源
    /// </summary>
    public static readonly FrozenSet<string> ExcludedDirs = ExcludedDirectoryCatalog.CodeIndexExcluded;

    /// <summary>
    /// 默认排除目录数组（用于数组初始化场景）
    /// 委托 ExcludedDirectoryCatalog.CodeIndexExcludedArray
    /// </summary>
    public static readonly string[] DefaultExcludedDirs = ExcludedDirectoryCatalog.CodeIndexExcludedArray;

    /// <summary>
    /// 检查路径中是否包含被排除的目录段
    /// 委托 ExcludedDirectoryCatalog.IsInExcludedDirectory
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>路径中包含排除目录段则返回 true</returns>
    public static bool IsInExcludedDirectory(string filePath)
        => ExcludedDirectoryCatalog.IsInExcludedDirectory(filePath);
}
