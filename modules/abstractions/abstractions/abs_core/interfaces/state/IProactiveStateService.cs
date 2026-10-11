namespace JoinCode.Abstractions.Interfaces;

public interface IProactiveStateService {
    /// <summary>获取是否处于活动状态。</summary>
    bool IsActive { get; }
    /// <summary>获取是否已暂停。</summary>
    bool IsPaused { get; }
    /// <summary>获取上下文是否被阻塞。</summary>
    bool IsContextBlocked { get; }
    /// <summary>激活主动服务。</summary>
    /// <param name="source">激活来源。</param>
    void Activate(string? source = null);
    /// <summary>停用主动服务。</summary>
    void Deactivate();
    /// <summary>暂停主动服务。</summary>
    void Pause();
    /// <summary>恢复主动服务。</summary>
    void Resume();
    /// <summary>设置上下文阻塞状态。</summary>
    /// <param name="blocked">是否阻塞。</param>
    void SetContextBlocked(bool blocked);
    event EventHandler? StateChanged;
}
