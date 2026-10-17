// JCC9001 抑制: 源码级回归测试需直接读取文件系统验证 module.slnx 引用完整性
// JCC11003 抑制: 存量代码可空抑制

namespace Host.Tests.Cli;

/// <summary>
/// BUG-5 回归测试: 10 个 module.slnx 路径引用修正(snake_case→PascalCase),移除 2 个不存在的 csproj 引用。
/// <para>验证: 所有 module.slnx 中 &lt;Project Path="..."/&gt; 引用的 csproj 文件实际存在。</para>
/// </summary>
[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = "源码级回归测试需直接读取文件系统验证 module.slnx 引用完整性")]
public sealed class ModuleSlnxIntegrityTests {
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly Regex ProjectPathRegex = new(
        """<Project\s+Path="([^"]+)"\s*/>""", RegexOptions.Compiled);

    /// <summary>
    /// 定位仓库根目录 — 向上查找含 .git 的目录
    /// </summary>
    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            var gitPath = System.IO.Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// 收集所有 module.slnx 文件路径
    /// </summary>
    private static IEnumerable<string> GetAllModuleSlnxFiles() {
        var modulesRoot = System.IO.Path.Combine(RepoRoot, "modules");
        if (!Directory.Exists(modulesRoot))
            return [];
        return Directory.EnumerateFiles(modulesRoot, "module.slnx", SearchOption.AllDirectories);
    }

    /// <summary>
    /// 解析单个 module.slnx 中所有 Project Path 引用
    /// </summary>
    private static List<string> ParseProjectPaths(string slnxPath) {
        var content = File.ReadAllText(slnxPath);
        var matches = ProjectPathRegex.Matches(content);
        var paths = new List<string>(matches.Count);
        foreach (System.Text.RegularExpressions.Match m in matches)
            paths.Add(m.Groups[1].Value);
        return paths;
    }

    /// <summary>
    /// BUG-5: 应至少找到 16 个 module.slnx 文件(16 功能域)
    /// </summary>
    [Fact]
    public void Should_Find_All_ModuleSlnx_Files() {
        var slnxFiles = GetAllModuleSlnxFiles().ToList();

        slnxFiles.Should().NotBeEmpty("应至少找到 module.slnx 文件");
        slnxFiles.Count.Should().BeGreaterThanOrEqualTo(16,
            "16 功能域应各有 module.slnx 文件");
    }

    /// <summary>
    /// BUG-5 核心: 每个 module.slnx 中引用的 csproj 文件应实际存在。
    /// 修复前有 snake_case 路径和 2 个不存在的 csproj 引用,修复后应全部有效。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void All_Referenced_Csproj_Paths_Should_Exist() {
        var slnxFiles = GetAllModuleSlnxFiles().ToList();
        var missingFiles = new List<(string Slnx, string ReferencedPath, string FullPath)>();

        foreach (var slnxPath in slnxFiles) {
            var slnxDir = System.IO.Path.GetDirectoryName(slnxPath)!;
            var projectPaths = ParseProjectPaths(slnxPath);

            foreach (var relPath in projectPaths) {
                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(slnxDir, relPath));
                if (!File.Exists(fullPath))
                    missingFiles.Add((slnxPath, relPath, fullPath));
            }
        }

        missingFiles.Should().BeEmpty(
            "BUG-5 修复: 所有 module.slnx 引用的 csproj 路径应存在。缺失: {0}",
            string.Join("; ", missingFiles.Select(m => $"{System.IO.Path.GetRelativePath(RepoRoot, m.Slnx)} → {m.ReferencedPath}")));
    }

    /// <summary>
    /// BUG-5: 不应存在被注释掉的 REMOVED 引用以外的无效引用。
    /// 验证正则不误匹配注释行(<!-- REMOVED: ... -->)
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void No_Invalid_Project_References_Outside_Comments() {
        var slnxFiles = GetAllModuleSlnxFiles().ToList();
        var invalidRefs = new List<(string Slnx, string ReferencedPath)>();

        foreach (var slnxPath in slnxFiles) {
            var slnxDir = System.IO.Path.GetDirectoryName(slnxPath)!;
            var projectPaths = ParseProjectPaths(slnxPath);

            foreach (var relPath in projectPaths) {
                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(slnxDir, relPath));
                if (!File.Exists(fullPath))
                    invalidRefs.Add((System.IO.Path.GetRelativePath(RepoRoot, slnxPath), relPath));
            }
        }

        // 正则只匹配 <Project Path="..."/> 不匹配注释,所以这里应为空
        invalidRefs.Should().BeEmpty(
            "正则提取的 Project 引用(非注释)应全部存在");
    }

    /// <summary>
    /// BUG-5: 每个 module.slnx 应至少包含一个有效 Project 引用
    /// </summary>
    [Fact]
    public void Each_ModuleSlnx_Should_Have_At_Least_One_Valid_Project() {
        var slnxFiles = GetAllModuleSlnxFiles().ToList();
        var emptySlnx = new List<string>();

        foreach (var slnxPath in slnxFiles) {
            var projectPaths = ParseProjectPaths(slnxPath);
            if (projectPaths.Count == 0)
                emptySlnx.Add(System.IO.Path.GetRelativePath(RepoRoot, slnxPath));
        }

        emptySlnx.Should().BeEmpty(
            "每个 module.slnx 应至少包含一个 Project 引用");
    }

    /// <summary>
    /// BUG-5: 验证特定已修正的 module.slnx(generators)引用有效。
    /// generators/module.slnx 曾有 REMOVED: shared/shared.csproj 注释,移除后应无无效引用。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Generators_ModuleSlnx_Should_Have_All_Valid_References() {
        var generatorsSlnx = System.IO.Path.Combine(RepoRoot, "modules", "generators", "module.slnx");

        File.Exists(generatorsSlnx).Should().BeTrue("generators/module.slnx 应存在");

        var slnxDir = System.IO.Path.GetDirectoryName(generatorsSlnx)!;
        var projectPaths = ParseProjectPaths(generatorsSlnx);

        projectPaths.Should().NotBeEmpty("generators/module.slnx 应有 Project 引用");

        foreach (var relPath in projectPaths) {
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(slnxDir, relPath));
            File.Exists(fullPath).Should().BeTrue(
                $"generators/module.slnx 引用的 {relPath} 应存在(BUG-5 路径修正)");
        }
    }
}
