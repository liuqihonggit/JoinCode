namespace JoinCode.ChatCommands;

/// <summary>
/// 会话选择器条目 — TUI 原生会话选择器的输入 DTO。
/// 对齐 ResumeCommand.SessionEntry（composition 层 internal record），
/// 由命令侧构造、TUI 侧消费，避免 composition 层类型泄漏到 abstractions 层。
/// </summary>
/// <param name="SessionId">会话唯一标识（完整 ID，显示时截断）。</param>
/// <param name="Preview">会话预览文本（customTitle 或 ID 前缀）。</param>
/// <param name="TimeAgo">相对时间描述（如"22 小时前"）。</param>
public sealed record SessionPickerEntry(string SessionId, string Preview, string TimeAgo);

/// <summary>
/// 会话选择器请求 — 包含会话列表和标题，作为 BeginSessionPicker 回调的入参。
/// </summary>
/// <param name="Entries">可恢复的会话条目列表（已排序、已过滤、已截断至上限）。</param>
/// <param name="Title">选择器标题（如"选择会话（当前仓库）"）。</param>
public sealed record SessionPickerRequest(IReadOnlyList<SessionPickerEntry> Entries, string Title);
