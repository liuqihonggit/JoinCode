
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 精简模式配置
/// </summary>
public sealed class SimpleModeConfig
{
    /// <summary>
    /// 使用简化提示词
    /// </summary>
    public bool UseSimplePrompts { get; init; } = true;

    /// <summary>
    /// 减少可用工具集
    /// </summary>
    public bool ReduceToolSet { get; init; } = true;

    /// <summary>
    /// 最小化 UI 显示
    /// </summary>
    public bool MinimalUI { get; init; } = true;

    /// <summary>
    /// 自动确认（跳过交互确认）
    /// </summary>
    public bool AutoConfirm { get; init; } = true;

    /// <summary>
    /// 隐藏加载动画
    /// </summary>
    public bool HideSpinner { get; init; }

    /// <summary>
    /// 隐藏状态栏
    /// </summary>
    public bool HideStatusBar { get; init; }

    /// <summary>
    /// 默认配置（启用精简模式时的默认行为）
    /// </summary>
    public static SimpleModeConfig Default => new();
}

/// <summary>
/// 精简模式变更事件参数
/// </summary>
public sealed class SimpleModeChangedEventArgs : EventArgs
{
    /// <summary>
    /// 是否处于精简模式
    /// </summary>
    public bool IsSimpleMode { get; init; }

    /// <summary>
    /// 当前精简模式配置
    /// </summary>
    public SimpleModeConfig Config { get; init; } = new();
}

/// <summary>
/// 精简模式服务接口 - 管理精简模式的启用/禁用状态和配置
/// </summary>
public interface ISimpleModeService
{
    /// <summary>
    /// 是否处于精简模式
    /// </summary>
    bool IsSimpleMode { get; }

    /// <summary>
    /// 启用精简模式
    /// </summary>
    void Enable();

    /// <summary>
    /// 禁用精简模式
    /// </summary>
    void Disable();

    /// <summary>
    /// 切换精简模式状态
    /// </summary>
    /// <returns>切换后的状态</returns>
    bool Toggle();

    /// <summary>
    /// 获取当前精简模式配置
    /// </summary>
    SimpleModeConfig GetCurrentConfig();

    /// <summary>
    /// 更新精简模式配置
    /// </summary>
    void UpdateConfig(SimpleModeConfig config);

    /// <summary>
    /// 精简模式状态变更事件
    /// </summary>
    event EventHandler<SimpleModeChangedEventArgs>? SimpleModeChanged;
}
