
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 创建团队命令
/// </summary>
public sealed record TeamCreateCommand(
    [Required(ErrorMessage = "team_name 不能为空")]
    [StringLength(100, ErrorMessage = "团队名称过长")]
    string TeamName,
    [StringLength(500, ErrorMessage = "团队描述过长")]
    string? Description,
    List<string>? InitialMembers);

/// <summary>
/// 删除团队命令
/// </summary>
public sealed record TeamDeleteCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId);

/// <summary>
/// 获取团队信息命令
/// </summary>
public sealed record TeamGetCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId);

/// <summary>
/// 列出团队命令
/// </summary>
public sealed record TeamListCommand;

/// <summary>
/// 添加团队成员命令
/// </summary>
public sealed record TeamAddMemberCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId,
    [Required(ErrorMessage = "agent_id 不能为空")]
    [StringLength(50, ErrorMessage = "代理ID过长")]
    string AgentId);

/// <summary>
/// 移除团队成员命令
/// </summary>
public sealed record TeamRemoveMemberCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId,
    [Required(ErrorMessage = "agent_id 不能为空")]
    [StringLength(50, ErrorMessage = "代理ID过长")]
    string AgentId);

/// <summary>
/// 发送消息命令
/// </summary>
public sealed record TeamSendMessageCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId,
    [Required(ErrorMessage = "sender_id 不能为空")]
    [StringLength(50, ErrorMessage = "发送者ID过长")]
    string SenderId,
    [Required(ErrorMessage = "content 不能为空")]
    [StringLength(2000, ErrorMessage = "消息内容过长")]
    string Content,
    [StringLength(50, ErrorMessage = "消息类型过长")]
    string? MessageType);

/// <summary>
/// 发送私信命令
/// </summary>
public sealed record TeamSendDirectMessageCommand(
    [Required(ErrorMessage = "target_agent_id 不能为空")]
    [StringLength(50, ErrorMessage = "目标代理ID过长")]
    string TargetAgentId,
    [Required(ErrorMessage = "sender_id 不能为空")]
    [StringLength(50, ErrorMessage = "发送者ID过长")]
    string SenderId,
    [Required(ErrorMessage = "content 不能为空")]
    [StringLength(2000, ErrorMessage = "消息内容过长")]
    string Content,
    [StringLength(50, ErrorMessage = "消息类型过长")]
    string? MessageType);

/// <summary>
/// 广播消息命令
/// </summary>
public sealed record TeamBroadcastMessageCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId,
    [Required(ErrorMessage = "sender_id 不能为空")]
    [StringLength(50, ErrorMessage = "发送者ID过长")]
    string SenderId,
    [Required(ErrorMessage = "content 不能为空")]
    [StringLength(2000, ErrorMessage = "消息内容过长")]
    string Content,
    [StringLength(50, ErrorMessage = "消息类型过长")]
    string? MessageType);

/// <summary>
/// 获取团队消息命令
/// </summary>
public sealed record TeamGetMessagesCommand(
    [Required(ErrorMessage = "team_id 不能为空")]
    [StringLength(50, ErrorMessage = "团队ID过长")]
    string TeamId,
    int? Limit);
