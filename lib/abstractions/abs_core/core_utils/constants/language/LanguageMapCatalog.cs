namespace JoinCode.Abstractions.Constants;

/// <summary>
/// 编程语言映射目录 — 文件扩展名到语言显示名的唯一数据源
/// 消除 SessionScanner/LspFileSync 等多处硬编码扩展名→语言名映射的重复定义。
///
/// 使用示例:
/// - LanguageMapCatalog.ExtensionToLanguage[".cs"] → "C#"
/// - LanguageMapCatalog.TryGetLanguage(".py", out var lang) → true, lang="Python"
/// </summary>
public static class LanguageMapCatalog {
    /// <summary>
    /// 文件扩展名到语言显示名的映射（OrdinalIgnoreCase）
    /// 唯一数据源:所有消费方通过此属性获取,禁止在消费方重复硬编码
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ExtensionToLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        [".cs"] = "C#",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".py"] = "Python",
        [".rb"] = "Ruby",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".java"] = "Java",
        [".md"] = "Markdown",
        [".json"] = "JSON",
        [".yaml"] = "YAML",
        [".yml"] = "YAML",
        [".sh"] = "Shell",
        [".css"] = "CSS",
        [".html"] = "HTML",
        [".ps1"] = "PowerShell",
        [".sql"] = "SQL",
        [".xml"] = "XML",
    };

    /// <summary>
    /// 尝试获取扩展名对应的语言显示名
    /// </summary>
    /// <param name="extension">文件扩展名（含点号，如 ".cs"）</param>
    /// <param name="language">输出的语言显示名</param>
    /// <returns>是否找到映射</returns>
    public static bool TryGetLanguage(string extension, out string language)
        => ExtensionToLanguage.TryGetValue(extension, out language!);

    /// <summary>
    /// 文件扩展名到 glob 搜索模式的映射（OrdinalIgnoreCase）
    /// 用于文件搜索:给定扩展名,返回匹配的 glob 模式列表(如 ".ts" → ["*.ts", "*.tsx"])
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ExtensionToGlobs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
        [".cs"] = ["*.cs"],
        [".ts"] = ["*.ts", "*.tsx"],
        [".js"] = ["*.js", "*.jsx"],
        [".py"] = ["*.py"],
        [".java"] = ["*.java"],
        [".go"] = ["*.go"],
        [".rs"] = ["*.rs"],
        [".cpp"] = ["*.cpp", "*.cc", "*.cxx"],
        [".c"] = ["*.c"],
        [".h"] = ["*.h", "*.hpp"],
        [".md"] = ["*.md"],
        [".json"] = ["*.json"],
        [".xml"] = ["*.xml"],
        [".yaml"] = ["*.yaml", "*.yml"],
    };
}
