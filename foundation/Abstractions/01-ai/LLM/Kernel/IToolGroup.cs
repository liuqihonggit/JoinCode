namespace JoinCode.Abstractions.LLM;

public interface IToolGroup
{
    string Name { get; }
    IReadOnlyList<IToolDef> Functions { get; }
}
