namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 单描述符规则泛型基类 — 子类继承 AnalyzerRuleBase&lt;T&gt; 后自动获得静态 Descriptor 字段。
/// T 是子类自身类型(奇异的递归模板模式),用于从 [AnalyzerRule] 特性提取元数据。
/// 多描述符规则直接实现 IAnalyzerRule 接口,用 RuleDescriptorFactory.CreateAll&lt;T&gt;() 获取 Id→Descriptor map。
/// </summary>
public abstract class AnalyzerRuleBase<T> : IAnalyzerRule where T : IAnalyzerRule {
    protected static readonly DiagnosticDescriptor Descriptor = RuleDescriptorFactory.CreateFirst<T>();

    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = new[] { Descriptor };

    public abstract void Register(CompilationStartAnalysisContext context, ProjectContext projectContext);
}
