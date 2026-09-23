namespace JccAuditCli;

/// <summary>
/// 文件过滤共享工具 — 所有 fixer/analyzer 统一调用，避免 ShouldSkipFile/IsTestFile/EnumerateCsFiles 各自维护。
/// 两级排除：
///   通用排除（所有工具）：bin/obj/.xxx/.git/.vs/artifacts/node_modules + 生成代码后缀
///   修复器排除（fixer 额外）：bcl_bridge/aot_safety.generator/aot_safety.shared + SyncFileReader.cs
/// </summary>
internal static class FileFilter {

    /// <summary>
    /// 通用排除目录（路径段匹配，大小写不敏感）— 所有工具共用
    /// </summary>
    private static readonly string[] s_commonExcludedDirs = new[] {
        "bin", "obj", ".xxx", ".git", ".vs", "artifacts", "node_modules", ".nuget"
    };

    /// <summary>
    /// 修复器额外排除目录 — fixer 不碰分析器自身源码和 BCL 桥接
    /// </summary>
    private static readonly string[] s_fixerExcludedDirs = new[] {
        "bcl_bridge", "aot_safety.generator", "aot_safety.shared"
    };

    /// <summary>
    /// 修复器额外排除文件名
    /// </summary>
    private static readonly string[] s_fixerExcludedFileNames = new[] {
        "SyncFileReader.cs"
    };

    /// <summary>
    /// 生成代码后缀
    /// </summary>
    private static readonly string[] s_generatedSuffixes = new[] {
        ".Designer.cs", ".Generated.cs", ".g.cs"
    };

    /// <summary>
    /// 判断是否应跳过该文件（修复器用）。
    /// 跳过条件：通用排除 + 修复器排除 + 生成代码。
    /// </summary>
    public static bool ShouldSkipFile(string? filePath) {
        if (string.IsNullOrEmpty(filePath)) return true;
        var normalized = filePath.Replace('\\', '/');

        if (HasExcludedDir(normalized, s_commonExcludedDirs)) return true;
        if (HasExcludedDir(normalized, s_fixerExcludedDirs)) return true;
        if (HasExcludedFileName(normalized, s_fixerExcludedFileNames)) return true;
        if (IsGeneratedFile(normalized)) return true;

        return false;
    }

    /// <summary>
    /// 判断是否应跳过该文件（审计工具用，可额外跳过测试）。
    /// 跳过条件：通用排除 + 生成代码 + 可选测试跳过。
    /// 不含修复器排除（审计工具可处理 bcl_bridge 等目录）。
    /// </summary>
    public static bool ShouldSkipFile(string? filePath, bool skipTests) {
        if (string.IsNullOrEmpty(filePath)) return true;
        var normalized = filePath.Replace('\\', '/');

        if (HasExcludedDir(normalized, s_commonExcludedDirs)) return true;
        if (IsGeneratedFile(normalized)) return true;

        if (skipTests) {
            if (normalized.Contains("/tests/", StringComparison.Ordinal) ||
                normalized.Contains("MockServer", StringComparison.Ordinal) ||
                normalized.Contains(".Tests.", StringComparison.Ordinal) ||
                normalized.Contains(".E2E.", StringComparison.Ordinal) ||
                normalized.Contains(".Benchmarks.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 判断是否是测试文件。
    /// </summary>
    public static bool IsTestFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains(".tests/") || normalized.Contains("/test/");
    }

    /// <summary>
    /// 枚举目录下所有 .cs 文件，跳过生成产物和排除文件（修复器用）。
    /// </summary>
    public static IEnumerable<string> EnumerateCsFiles(string rootPath) {
        return Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldSkipFile(f));
    }

    private static bool HasExcludedDir(string normalizedPath, string[] excludedDirs) {
        var segments = normalizedPath.Split('/');
        foreach (var segment in segments) {
            if (excludedDirs.Contains(segment, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasExcludedFileName(string normalizedPath, string[] excludedNames) {
        var fileName = Path.GetFileName(normalizedPath);
        return excludedNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedFile(string normalizedPath) {
        foreach (var suffix in s_generatedSuffixes) {
            if (normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
