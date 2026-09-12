namespace JoinCode.Abstractions.LLM;

public interface IToolGroup
{
    string Name { get; }
    IEnumerable<IToolDef> Functions { get; }
}
