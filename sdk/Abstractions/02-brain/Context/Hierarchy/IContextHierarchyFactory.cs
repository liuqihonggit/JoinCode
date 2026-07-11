namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextHierarchyFactory
{
    IContextHierarchy Create(ContextHierarchyOptions options);
}
