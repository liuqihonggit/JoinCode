namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 分析器规则特性 — 标记每个规则类,声明诊断元数据(Id/Title/Description/Category/Severity 等)。
/// AllowMultiple=true 支持多描述符规则(如 DictionaryObjectRule 有 JCC1001/1002/1003 三个特性)。
/// 替代手动 new DiagnosticDescriptor(...),由 RuleDescriptorFactory 从特性提取创建。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class AnalyzerRuleAttribute : Attribute {
    public string Id { get; set; } = "";
    public string AnalyzerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Warning;
    public bool IsEnabledByDefault { get; set; } = true;
    public string HelpLinkUri { get; set; } = "";
    public bool IsCompilationEnd { get; set; } = false;
}

/// <summary>
/// MSBuild ProjectType 属性映射的枚举 — 由 build_property.ProjectType 标签驱动。
/// </summary>
public enum ProjectType {
    Unknown,
    Library,
    Test,
    Ui,
    App,
    Generator,
    Tool
}

/// <summary>
/// 项目上下文 — 从 Compilation + AnalyzerConfigOptionsProvider 提取 ProjectType 标签。
/// </summary>
public sealed class ProjectContext {
    public ProjectType ProjectType { get; set; } = ProjectType.Unknown;
    /// <summary>非测试程序集是否已中央登记 ModuleInitializer（csproj &lt;JccAllowModuleInitializer&gt;true），JCC9312 消费。</summary>
    public bool AllowModuleInitializer { get; set; }
    /// <summary>项目目录（build_property.ProjectDir），用于区分契约域与非交付工具。</summary>
    public string ProjectDirectory { get; set; } = "";
    /// <summary>域身份（build_property.JccDomain），18 个契约域之一；测试/非交付工具为空。</summary>
    public string Domain { get; set; } = "";
    /// <summary>允许依赖的域清单（build_property.JccAllowedDomains，逗号/分号分隔）。</summary>
    public string AllowedDomains { get; set; } = "";
    /// <summary>域默认 API 可见性（build_property.JccApiVisibility，internal/public）。</summary>
    public string ApiVisibility { get; set; } = "";
    /// <summary>允许的类型命名空间根（build_property.JccNamespaceRoots，分号分隔）。</summary>
    public string NamespaceRoots { get; set; } = "";
    /// <summary>允许的 IVT 目标（build_property.JccAllowInternalsVisibleTo，分号分隔）。</summary>
    public string AllowInternalsVisibleTo { get; set; } = "";
    /// <summary>允许排除出 module.slnx 的项目（build_property.JccSlnxExcluded，分号分隔）。</summary>
    public string SlnxExcluded { get; set; } = "";
    public bool IsLibrary => ProjectType == ProjectType.Library;
    public bool IsTest => ProjectType == ProjectType.Test;
    public bool IsUi => ProjectType == ProjectType.Ui;
    public bool IsApp => ProjectType == ProjectType.App;
    public bool IsGenerator => ProjectType == ProjectType.Generator;
    public bool IsTool => ProjectType == ProjectType.Tool;

    public static ProjectContext From(Compilation compilation, AnalyzerConfigOptionsProvider optionsProvider) {
        var raw = "";
        var allowModuleInitializer = false;
        var projectDirectory = "";
        var domain = "";
        var allowedDomains = "";
        var apiVisibility = "";
        var namespaceRoots = "";
        var allowInternalsVisibleTo = "";
        var slnxExcluded = "";

        foreach (var tree in compilation.SyntaxTrees) {
            var options = optionsProvider.GetOptions(tree);
            Capture(options, "build_property.ProjectType", ref raw);
            Capture(options, "build_property.ProjectDir", ref projectDirectory);
            Capture(options, "build_property.JccDomain", ref domain);
            Capture(options, "build_property.JccAllowedDomains", ref allowedDomains);
            Capture(options, "build_property.JccApiVisibility", ref apiVisibility);
            Capture(options, "build_property.JccNamespaceRoots", ref namespaceRoots);
            Capture(options, "build_property.JccAllowInternalsVisibleTo", ref allowInternalsVisibleTo);
            Capture(options, "build_property.JccSlnxExcluded", ref slnxExcluded);
            if (!allowModuleInitializer
                && options.TryGetValue("build_property.JccAllowModuleInitializer", out var allow)
                && bool.TryParse(allow, out var parsed)) {
                allowModuleInitializer = parsed;
            }
        }

        return new ProjectContext {
            ProjectType = ParseProjectType(raw),
            AllowModuleInitializer = allowModuleInitializer,
            ProjectDirectory = projectDirectory,
            Domain = domain,
            AllowedDomains = allowedDomains,
            ApiVisibility = apiVisibility,
            NamespaceRoots = namespaceRoots,
            AllowInternalsVisibleTo = allowInternalsVisibleTo,
            SlnxExcluded = slnxExcluded,
        };
    }

    private static void Capture(AnalyzerConfigOptions options, string key, ref string target) {
        if (target.Length > 0) return;
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) {
            target = value;
        }
    }

    private static ProjectType ParseProjectType(string raw) {
        return raw switch {
            "library" => ProjectType.Library,
            "test" => ProjectType.Test,
            "ui" => ProjectType.Ui,
            "app" => ProjectType.App,
            "generator" => ProjectType.Generator,
            "tool" => ProjectType.Tool,
            _ => ProjectType.Unknown
        };
    }
}

/// <summary>
/// 分析器规则接口 — 每个规则一个类一个文件,实现此接口。
/// Descriptors: 该规则声明的所有诊断描述符(通常1个,多描述符规则有多个)。
/// Register: 在 CompilationStartAnalysisContext 上注册语法节点动作。
/// </summary>
public interface IAnalyzerRule {
    IReadOnlyList<DiagnosticDescriptor> Descriptors { get; }
    void Register(CompilationStartAnalysisContext context, ProjectContext projectContext);
}
