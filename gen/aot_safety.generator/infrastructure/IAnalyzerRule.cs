namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 分析器规则特性 — 标记每个规则类,声明诊断元数据(Id/Title/Description/Category/Severity 等)。
/// 替代手动 new DiagnosticDescriptor(...),由 RuleDescriptorFactory 从特性提取创建。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnalyzerRuleAttribute : Attribute {
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Warning;
    public bool IsEnabledByDefault { get; set; } = true;
    public string HelpLinkUri { get; set; } = "";
}

/// <summary>
/// MSBuild ProjectType 属性映射的枚举 — 由 build_property.ProjectType 标签驱动,替代程序集名/引用猜测。
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
/// 项目上下文 — 从 Compilation + AnalyzerConfigOptionsProvider 提取 ProjectType 标签,提供给每个规则做项目类型判断。
/// </summary>
public sealed class ProjectContext {
    public ProjectType ProjectType { get; set; } = ProjectType.Unknown;
    public bool IsLibrary => ProjectType == ProjectType.Library;
    public bool IsTest => ProjectType == ProjectType.Test;
    public bool IsUi => ProjectType == ProjectType.Ui;
    public bool IsApp => ProjectType == ProjectType.App;
    public bool IsGenerator => ProjectType == ProjectType.Generator;
    public bool IsTool => ProjectType == ProjectType.Tool;

    public static ProjectContext From(Compilation compilation, AnalyzerConfigOptionsProvider optionsProvider) {
        var raw = "";
        foreach (var tree in compilation.SyntaxTrees) {
            if (optionsProvider.GetOptions(tree).TryGetValue("build_property.ProjectType", out var value)) {
                raw = value ?? "";
                break;
            }
        }
        return new ProjectContext { ProjectType = ParseProjectType(raw) };
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
/// Descriptors: 该规则声明的所有诊断描述符(通常1个,TaskDelayInTestsRule有3个)。
/// Register: 在 CompilationStartAnalysisContext 上注册语法节点动作。
/// </summary>
public interface IAnalyzerRule {
    IReadOnlyList<DiagnosticDescriptor> Descriptors { get; }
    void Register(CompilationStartAnalysisContext context, ProjectContext projectContext);
}
