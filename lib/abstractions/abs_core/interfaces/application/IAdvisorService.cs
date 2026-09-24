namespace JoinCode.Abstractions.Interfaces;

public interface IAdvisorService : IAsyncDisposable {
    /// <summary>获取顾问模型标识。</summary>
    string? AdvisorModel { get; }
    /// <summary>设置顾问模型。</summary>
    void SetAdvisorModel(string modelId);
    /// <summary>清除顾问模型。</summary>
    void ClearAdvisorModel();
    /// <summary>获取顾问是否启用。</summary>
    bool IsAdvisorEnabled { get; }
}