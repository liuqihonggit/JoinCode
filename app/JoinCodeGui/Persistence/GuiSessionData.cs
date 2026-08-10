
namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化数据 — JSON 形状对齐 CLI 的 SessionData（PascalCase 序列化），
/// 写入同一 sessions 目录（~/.jcc/sessions/{Id}.json），使 CLI /resume 与 GUI 侧边栏共享同一会话文件。
/// </summary>
public sealed class GuiSessionData
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string CustomTitle { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<GuiSessionMessage> Messages { get; set; } = [];
}

/// <summary>
/// 会话中的单条消息 — 对齐 CLI SessionMessage（ChatMessage 的 Role+Content+Timestamp）。
/// </summary>
public sealed class GuiSessionMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 会话列表摘要（不含消息内容，用于侧边栏快速加载）
/// </summary>
public sealed class GuiSessionSummary
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public DateTime LastModified { get; init; }

    public int MessageCount { get; init; }
}
