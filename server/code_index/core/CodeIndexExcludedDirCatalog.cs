namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引排除目录目录 — 强制跳过的目录名唯一数据源
/// 消除 FileWatcherIntegration/IncrementalUpdater/CodeIndexer 三处硬编码 { "bin", "obj", ".git", ".x" } 的重复定义。
///
/// 排除原因:
/// - bin/obj: .NET 编译产物,扫描无意义且体积大
/// - .git: Git 内部目录,扫描无意义
/// - .x: 临时归档目录(见 AGENTS.md .xxx/ 归档规则)
/// </summary>
public static class CodeIndexExcludedDirCatalog {
    /// <summary>
    /// 强制排除的目录名集合（OrdinalIgnoreCase）
    /// 唯一数据源:所有消费方通过此属性获取,禁止在消费方重复硬编码
    /// </summary>
    public static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".x" };

    /// <summary>
    /// 默认排除目录数组（用于数组初始化场景）
    /// </summary>
    public static readonly string[] DefaultExcludedDirs = ["bin", "obj", ".git", ".x"];

    /// <summary>
    /// 检查路径中是否包含被排除的目录段
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>路径中包含排除目录段则返回 true</returns>
    public static bool IsInExcludedDirectory(string filePath) {
        var span = filePath.AsSpan();
        while (!span.IsEmpty) {
            var idx = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segment = idx < 0 ? span : span[..idx];
            if (!segment.IsEmpty && ExcludedDirs.Contains(segment.ToString())) {
                return true;
            }
            span = idx < 0 ? [] : span[(idx + 1)..];
        }
        return false;
    }
}
