namespace JoinCode.Abstractions.LLM;

public interface IToolDef
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<IToolParam> Parameters { get; }
}
