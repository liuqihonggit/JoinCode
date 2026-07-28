namespace JoinCode.Abstractions.LLM;

public interface IExecutionSettingsProvider
{
    EffortLevel EffortLevel { get; set; }
    bool FastMode { get; }
    string? FastModelId { get; }
}
