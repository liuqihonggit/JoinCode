namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextHierarchyFactory {
    /// <summary>创建上下文层级。</summary>
    IContextHierarchy Create(ContextHierarchyOptions options);
}