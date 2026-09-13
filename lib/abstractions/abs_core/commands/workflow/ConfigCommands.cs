
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 配置获取命令
/// </summary>
public sealed record ConfigGetCommand(
    [Required(ErrorMessage = "key 不能为空")]
    [StringLength(256, ErrorMessage = "配置键名过长")]
    string Key);

/// <summary>
/// 配置设置命令
/// </summary>
public sealed record ConfigSetCommand(
    [Required(ErrorMessage = "key 不能为空")]
    [StringLength(256, ErrorMessage = "配置键名过长")]
    string Key,
    [Required(ErrorMessage = "value 不能为空")]
    string Value);

/// <summary>
/// 配置列表命令
/// </summary>
public sealed record ConfigListCommand;
