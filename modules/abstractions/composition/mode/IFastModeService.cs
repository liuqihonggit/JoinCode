namespace JoinCode.Abstractions.Interfaces;

public interface IFastModeService {
    /// <summary>获取快速模式是否激活。</summary>
    bool IsFastModeActive { get; }

    /// <summary>获取快速模型标识。</summary>
    string FastModelId { get; }

    /// <summary>获取主模型标识。</summary>
    string PrimaryModelId { get; }

    /// <summary>激活快速模式。</summary>
    void Activate();

    /// <summary>停用快速模式。</summary>
    void Deactivate();

    /// <summary>切换快速模式状态。</summary>
    void Toggle();

    /// <summary>设置快速模型。</summary>
    /// <param name="modelId">模型标识。</param>
    void SetFastModel(string modelId);

    /// <summary>设置主模型。</summary>
    /// <param name="modelId">模型标识。</param>
    void SetPrimaryModel(string modelId);

    event EventHandler<FastModeChangedEventArgs>? FastModeChanged;
}

public sealed class FastModeChangedEventArgs : EventArgs {
    /// <summary>获取快速模式是否激活。</summary>
    public bool IsFastModeActive { get; init; }
    /// <summary>获取激活的模型标识。</summary>
    public string ActiveModelId { get; init; } = string.Empty;
    /// <summary>获取未激活的模型标识。</summary>
    public string InactiveModelId { get; init; } = string.Empty;
}
