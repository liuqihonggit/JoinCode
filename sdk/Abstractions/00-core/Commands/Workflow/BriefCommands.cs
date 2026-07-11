
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 简要模式切换命令
/// </summary>
public sealed record BriefModeCommand(
    [Required(ErrorMessage = "enabled 参数必须指定")]
    bool Enabled);

/// <summary>
/// 简要模式状态命令
/// </summary>
public sealed record BriefStatusCommand;
