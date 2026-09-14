
namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化数据 — JSON 形状对齐 CLI 的 SessionData（PascalCase 序列化），
/// 写入同一 sessions 目录（~/.jcc/sessions/{Id}.json），使 CLI /resume 与 GUI 侧边栏共享同一会话文件。
/// </summary>
public sealed class GuiSessionData
{
    /// <summary>会话唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>项目路径</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>自定义标题</summary>
    public string CustomTitle { get; set; } = string.Empty;

    /// <summary>模型标识</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>供应商名称</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>消息列表</summary>
    public List<GuiSessionMessage> Messages { get; set; } = [];
}

/// <summary>
/// 会话中的单条消息 — 对齐 CLI SessionMessage（ChatMessage 的 Role+Content+Timestamp）。
/// </summary>
public sealed class GuiSessionMessage
{
    /// <summary>消息角色</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 会话列表摘要（不含消息内容，用于侧边栏快速加载）
/// </summary>
public sealed class GuiSessionSummary
{
    /// <summary>会话唯一标识</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>会话标题</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>最后修改时间</summary>
    public DateTime LastModified { get; init; }

    /// <summary>消息数量</summary>
    public int MessageCount { get; init; }
}
