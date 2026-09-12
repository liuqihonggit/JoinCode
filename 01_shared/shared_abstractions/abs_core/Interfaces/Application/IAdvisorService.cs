namespace JoinCode.Abstractions.Interfaces;

public interface IAdvisorService : IDisposable
{
    string? AdvisorModel { get; }
    void SetAdvisorModel(string modelId);
    void ClearAdvisorModel();
    bool IsAdvisorEnabled { get; }
}
