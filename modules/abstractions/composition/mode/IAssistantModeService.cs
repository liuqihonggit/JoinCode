namespace JoinCode.Abstractions.Interfaces;

public interface IAssistantModeService {
    /// <summary>获取是否为助手模式。</summary>
    bool IsAssistantMode { get; }
    /// <summary>获取助手模式是否启用。</summary>
    bool IsAssistantModeEnabled { get; }
}