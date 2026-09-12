namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 简要模式状态
/// </summary>
public readonly record struct BriefModeStatus
{
    public required bool IsEnabled { get; init; }
    public required string Description { get; init; }
    public DateTime? EnabledAt { get; init; }

    public static BriefModeStatus Enabled(DateTime enabledAt)
    {
        return new BriefModeStatus
        {
            IsEnabled = true,
            Description = "简要模式已启用 - 将使用精简输出，减少详细信息的显示",
            EnabledAt = enabledAt
        };
    }

    public static BriefModeStatus Disabled()
    {
        return new BriefModeStatus
        {
            IsEnabled = false,
            Description = "简要模式已禁用 - 将使用完整输出，显示所有详细信息",
            EnabledAt = null
        };
    }
}

/// <summary>
/// 简要模式服务接口 - 管理简要模式的启用/禁用状态
/// </summary>
public interface IBriefModeService
{
    /// <summary>
    /// 是否启用简要模式
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 启用时间
    /// </summary>
    DateTime? EnabledAt { get; }

    /// <summary>
    /// 用户显式 opt-in — 对齐 TS userMsgOptIn
    /// 当用户通过 /brief 命令或 --brief 标志启用时设为 true
    /// </summary>
    bool UserMsgOptIn { get; set; }

    /// <summary>
    /// 启用简要模式
    /// </summary>
    void Enable();

    /// <summary>
    /// 禁用简要模式
    /// </summary>
    void Disable();

    /// <summary>
    /// 切换简要模式状态
    /// </summary>
    /// <returns>切换后的状态</returns>
    bool Toggle();

    /// <summary>
    /// 获取简要模式状态
    /// </summary>
    /// <returns>状态信息</returns>
    BriefModeStatus GetStatus();
}
