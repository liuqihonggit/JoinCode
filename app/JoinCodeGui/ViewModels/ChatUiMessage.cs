using System.ComponentModel;

using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Models.Diff;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// UI 会话消息模型 — ViewModel 将引擎事件组装为可展示的对话消息。
/// 仅承载展示所需字段，不直接暴露引擎内部类型。
/// 实现 <see cref="INotifyPropertyChanged"/> 以便思考消息折叠/展开时刷新 UI。
/// </summary>
public sealed class ChatUiMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required MessageRole Role { get; init; }
    private string _content = string.Empty;
    public required string Content
    {
        get => _content;
        set
        {
            if (_content == value)
                return;
            _content = value;
            Raise(nameof(Content));
            Raise(nameof(ThinkingSummary));
        }
    }
    public DateTime Timestamp { get; init; }
    /// <summary>是否流式追加中——用于 UI 显示"输出中"状态</summary>
    public bool IsStreaming { get; set; }

    /// <summary>角色显示名（User/Assistant/System 的中文标签）</summary>
    public string RoleLabel => Role switch
    {
        MessageRole.User => "你",
        MessageRole.Assistant => "AI",
        MessageRole.System => "系统",
        _ => "未知"
    };

    /// <summary>角色气泡展示方向：Assistant 在左，User 在右</summary>
    public bool IsUser => Role == MessageRole.User;

    /// <summary>消息展示类型（正文/思考/工具调用/工具结果）</summary>
    public ChatUiMessageKind Kind { get; set; } = ChatUiMessageKind.Text;

    /// <summary>工具名（Kind=ToolCall/ToolResult 时携带）</summary>
    public string? ToolName { get; set; }

    /// <summary>工具调用参数（Kind=ToolCall 时携带）</summary>
    public string? ToolArguments { get; set; }

    /// <summary>工具执行结果（Kind=ToolResult 时携带）</summary>
    public string? ToolResultText { get; set; }

    /// <summary>工具执行是否出错（Kind=ToolResult 时携带，驱动红色提示）</summary>
    public bool IsToolError { get; set; }

    /// <summary>工具调用开始时间（Kind=ToolCall 时记录，用于计算已运行时长）</summary>
    public DateTime? ToolStartTime { get; set; }

    /// <summary>工具是否仍在运行（驱动倒计时显示；ToolCallEnd 后置 false）</summary>
    private bool _isToolRunning;
    public bool IsToolRunning
    {
        get => _isToolRunning;
        set
        {
            if (_isToolRunning == value)
                return;
            _isToolRunning = value;
            Raise(nameof(IsToolRunning));
        }
    }

    /// <summary>已运行时长展示文本（如 "⏱ 1.2s"，由 View 层计时器定期刷新）</summary>
    private string _toolElapsedText = string.Empty;
    public string ToolElapsedText
    {
        get => _toolElapsedText;
        set
        {
            if (_toolElapsedText == value)
                return;
            _toolElapsedText = value;
            Raise(nameof(ToolElapsedText));
        }
    }

    /// <summary>根据 ToolStartTime 刷新已运行时长文本（由 View 层计时器调用）</summary>
    public void RefreshElapsed()
    {
        if (ToolStartTime is { } start)
        {
            var elapsed = DateTime.Now - start;
            ToolElapsedText = elapsed.TotalSeconds < 60
                ? $"⏱ {elapsed.TotalSeconds:F1}s"
                : $"⏱ {(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }
    }

    /// <summary>结构化 Patch（ToolCallEnd 携带，驱动 Diff 渲染；仅文件编辑类工具填充）</summary>
    public StructuredPatchHunk[]? StructuredPatch { get; set; }

    /// <summary>是否有 Diff 可渲染</summary>
    public bool HasDiff => StructuredPatch is { Length: > 0 };

    /// <summary>是否为一条工具调用消息</summary>
    public bool IsToolCall => Kind is ChatUiMessageKind.ToolCall or ChatUiMessageKind.ToolResult;

    /// <summary>是否为工具调用启动消息（显示调用参数）</summary>
    public bool IsToolCallStart => Kind == ChatUiMessageKind.ToolCall;

    /// <summary>是否为工具结果消息（显示结果文本）</summary>
    public bool IsToolResultMessage => Kind == ChatUiMessageKind.ToolResult;

    /// <summary>是否为思考过程消息</summary>
    public bool IsThinking => Kind == ChatUiMessageKind.Thinking;

    /// <summary>思考消息是否已展开（终端式默认全展开，实时可见思考流；可手动收起）</summary>
    private bool _isThinkingExpanded = true;
    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set
        {
            if (_isThinkingExpanded == value)
                return;
            _isThinkingExpanded = value;
            Raise(nameof(IsThinkingExpanded));
            Raise(nameof(IsThinkingCollapsed));
        }
    }

    /// <summary>思考消息处于折叠态（未展开）；驱动折叠提示可见性</summary>
    public bool IsThinkingCollapsed => IsThinking && !IsThinkingExpanded;

    /// <summary>正文区域可见性：工具消息隐藏（走工具面板）；思考消息折叠时隐藏展示摘要提示，展开后可见</summary>
    public bool ShowBody => !IsToolCall && (!IsThinking || IsThinkingExpanded);

    /// <summary>折叠时的摘要文案：非思考消息为空；思考消息展示简要提示并可点击展开</summary>
    public string ThinkingSummary => IsThinking ? $"点击展开，思考 {Content.Length} 字" : string.Empty;

    /// <summary>类型标签（思考🧠 / 工具🛠 / 结果✅，正文为空）</summary>
    public string KindLabel => Kind switch
    {
        ChatUiMessageKind.Thinking => "🧠 思考",
        ChatUiMessageKind.ToolCall => "🛠 工具调用",
        ChatUiMessageKind.ToolResult => "✅ 工具结果",
        _ => string.Empty
    };

    /// <summary>正文展示文本：工具结果优先展示结果内容，其余展示 Content</summary>
    public string DisplayText => Kind == ChatUiMessageKind.ToolResult
        ? (ToolResultText ?? string.Empty)
        : (Content ?? string.Empty);

    /// <summary>内容是否为代码块（以 ``` 包裹，行代码卡片高亮展示）</summary>
    public bool IsCodeBlock => Content.TrimStart().StartsWith("```", StringComparison.Ordinal)
        || Content.TrimStart().StartsWith("#include", StringComparison.Ordinal)
        || Content.TrimStart().StartsWith("using System", StringComparison.Ordinal);
}