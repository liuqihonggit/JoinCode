namespace JoinCode.Abstractions.Interfaces;

public interface IFastModeService
{
    bool IsFastModeActive { get; }

    string FastModelId { get; }

    string PrimaryModelId { get; }

    void Activate();

    void Deactivate();

    void Toggle();

    void SetFastModel(string modelId);

    void SetPrimaryModel(string modelId);

    event EventHandler<FastModeChangedEventArgs>? FastModeChanged;
}

public sealed class FastModeChangedEventArgs : EventArgs
{
    public bool IsFastModeActive { get; init; }
    public string ActiveModelId { get; init; } = string.Empty;
    public string InactiveModelId { get; init; } = string.Empty;
}
