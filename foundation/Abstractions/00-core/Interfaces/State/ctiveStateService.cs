namespace JoinCode.Abstractions.Interfaces;

public interface IProactiveStateService
{
    bool IsActive { get; }
    bool IsPaused { get; }
    bool IsContextBlocked { get; }
    void Activate(string? source = null);
    void Deactivate();
    void Pause();
    void Resume();
    void SetContextBlocked(bool blocked);
    event EventHandler? StateChanged;
}
