namespace JoinCode.Abstractions.Constants;

/// <summary>
/// 排除目录目录 — 强制跳过的目录名唯一数据源
/// 消除 SessionInitStep/ProjectStructureRule/FileFilter/HotFileDetector/MarkdownWalker/CodeIndexExcludedDirCatalog
/// 多处硬编码排除目录列表的重复定义。
///
/// 分层集合(核心 → 扩展),每层基于上层追加,避免重复定义:
/// - <see cref="CodeIndexExcluded"/>: 核心集(4 个) — bin/obj/.git/.x
/// - <see cref="SearchExcluded"/>: 搜索集(7 个) — CodeIndexExcluded + .vs/.idea/node_modules
/// - <see cref="HotFileExcluded"/>: 热文件集(17 个) — SearchExcluded + .vscode/.svn/__pycache__/.gradle/build/dist/target/artifacts/.codegraph/.jcc
/// - <see cref="MarkdownWalkExcluded"/>: Markdown 遍历集(13 个) — CodeIndexExcluded + .svn/.hg/node_modules/.vs/.vscode/.idea/dist/build/out
/// - <see cref="AuditExcluded"/>: 审计集(9 个) — CodeIndexExcluded + .xxx/.vs/artifacts/node_modules/.nuget
///
/// 排除原因:
/// - bin/obj: .NET 编译产物,扫描无意义且体积大
/// - .git: Git 内部目录,扫描无意义
/// - .x: 临时归档目录(见 AGENTS.md .xxx/ 归档规则)
/// - .vs/.vscode/.idea: IDE 配置目录
/// - node_modules: npm 依赖目录,体积大
/// - .svn/.hg/.bzr/.jj/.sl: 其他 VCS 目录
/// - __pycache__/.gradle: Python/Gradle 缓存
/// - build/dist/target/out: 通用构建输出
/// - artifacts: 通用制品目录
/// - .codegraph/.jcc: 工具内部目录
/// - .xxx: 归档目录(AGENTS.md)
/// - .nuget: NuGet 缓存
///
/// 单数据源+委托消费模式:所有消费方通过此 catalog 获取,禁止在消费方重复硬编码。
/// 例外: Roslyn 分析器(gen/aot_safety.generator)因独立性约束无法引用 abstractions,
/// 需本地维护与 <see cref="SearchExcluded"/> 同步的集合(见 ProjectStructureRule.cs 注释)。
/// </summary>
public static class ExcludedDirectoryCatalog {
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// 核心排除集源数据(4 个) — bin/obj/.git/.x
    /// </summary>
    private static readonly string[] CoreArray = ["bin", "obj", ".git", ".x"];

    /// <summary>
    /// 核心排除集(4 个) — bin/obj/.git/.x(OrdinalIgnoreCase)
    /// 唯一数据源:CodeIndexExcludedDirCatalog 及其他核心消费方通过此属性获取
    /// </summary>
    public static readonly FrozenSet<string> CodeIndexExcluded = CoreArray.ToFrozenSet(Comparer);

    /// <summary>
    /// 核心排除集数组形式(用于数组初始化场景,如 CodeIndexer foreach)
    /// </summary>
    public static readonly string[] CodeIndexExcludedArray = CoreArray;

    /// <summary>
    /// 搜索排除集(7 个) — CodeIndexExcluded + .vs/.idea/node_modules
    /// 用于项目结构分析等搜索场景(对齐 ProjectStructureRule 原 7 个集合)
    /// </summary>
    public static readonly FrozenSet<string> SearchExcluded = BuildSet(CoreArray, ".vs", ".idea", "node_modules");

    /// <summary>
    /// 搜索排除集数组形式
    /// </summary>
    public static readonly string[] SearchExcludedArray = SearchExcluded.ToArray();

    /// <summary>
    /// 热文件排除集(17 个) — SearchExcluded + .vscode/.svn/__pycache__/.gradle/build/dist/target/artifacts/.codegraph/.jcc
    /// 用于 HotFileDetector(对齐原 16 个 + .x 归档目录)
    /// </summary>
    public static readonly FrozenSet<string> HotFileExcluded = BuildSet(
        SearchExcluded,
        ".vscode", ".svn", "__pycache__", ".gradle", "build", "dist", "target", "artifacts", ".codegraph", ".jcc");

    /// <summary>
    /// 热文件排除集数组形式
    /// </summary>
    public static readonly string[] HotFileExcludedArray = HotFileExcluded.ToArray();

    /// <summary>
    /// Markdown 遍历排除集(13 个) — CodeIndexExcluded + .svn/.hg/node_modules/.vs/.vscode/.idea/dist/build/out
    /// 用于 MarkdownWalker(对齐原 12 个 + .x 归档目录)
    /// </summary>
    public static readonly FrozenSet<string> MarkdownWalkExcluded = BuildSet(
        CoreArray,
        ".svn", ".hg", "node_modules", ".vs", ".vscode", ".idea", "dist", "build", "out");

    /// <summary>
    /// Markdown 遍历排除集数组形式
    /// </summary>
    public static readonly string[] MarkdownWalkExcludedArray = MarkdownWalkExcluded.ToArray();

    /// <summary>
    /// 审计排除集(9 个) — CodeIndexExcluded + .xxx/.vs/artifacts/node_modules/.nuget
    /// 用于 jcc_audit_ast_cli FileFilter(对齐原 8 个 + .x 归档目录)
    /// </summary>
    public static readonly FrozenSet<string> AuditExcluded = BuildSet(
        CoreArray,
        ".xxx", ".vs", "artifacts", "node_modules", ".nuget");

    /// <summary>
    /// 审计排除集数组形式
    /// </summary>
    public static readonly string[] AuditExcludedArray = AuditExcluded.ToArray();

    /// <summary>
    /// 检查路径中是否包含被排除的目录段(基于 <see cref="CodeIndexExcluded"/>)
    /// 复用 CodeIndexExcludedDirCatalog.IsInExcludedDirectory 的实现逻辑(Span 切片,0-GC)
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>路径中包含排除目录段则返回 true</returns>
    public static bool IsInExcludedDirectory(string filePath) {
        var span = filePath.AsSpan();
        while (!span.IsEmpty) {
            var idx = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segment = idx < 0 ? span : span[..idx];
            if (!segment.IsEmpty && CodeIndexExcluded.Contains(segment.ToString())) {
                return true;
            }
            span = idx < 0 ? [] : span[(idx + 1)..];
        }
        return false;
    }

    private static FrozenSet<string> BuildSet(IEnumerable<string> baseSet, params string[] extras) {
        var hash = new HashSet<string>(baseSet, Comparer);
        foreach (var e in extras) {
            hash.Add(e);
        }
        return hash.ToFrozenSet(Comparer);
    }
}
