namespace JoinCode.Abstractions.LLM;

public interface IToolParam
{
    string Name { get; }
    string Description { get; }
    Type? ParameterType { get; }
    bool IsRequired { get; }
}
