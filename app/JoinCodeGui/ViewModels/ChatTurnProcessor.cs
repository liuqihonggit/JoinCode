using System.Text;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 单回合事件→UI 消息组装器 — 从 MainViewModel 抽取的纯组装逻辑（无引擎依赖，可单测）。
/// 职责：助手占位创建、正文/思考流式缓冲、工具卡片生命周期、子代理运行组卡片聚合、终局收尾。
/// 不负责：事件迭代与取消、RunStatus 心跳/token 上报（MainViewModel 围绕本类调用）、窗口交互。
/// </summary>
internal sealed class ChatTurnProcessor
{
    private readonly ObservableCollection<ChatUiMessage> _messages;

    // === 子代理运行态 ===
    private SubAgentRunTracker _agentTracker = new();
    private Dictionary<string, AgentRunVm> _agentRunVms = new(StringComparer.Ordinal);
    private ChatUiMessage? _agentGroupCard;

    // === 回合内状态 ===
    private int _assistantIndex;
    private ChatUiMessage? _currentThinking;
    private ChatUiMessage? _currentToolCall;
    private readonly StringBuilder _textBuilder = new();
    private readonly StringBuilder _thinkingBuilder = new();

    /// <summary>本轮真实 token 用量合计（Complete 事件累加；引擎未上报为 0）</summary>
    public long TotalTokens { get; private set; }

    /// <summary>助手占位消息（BeginTurn 创建；流式期间实时刷新 Content）</summary>
    public ChatUiMessage AssistantPlaceholder { get; private set; } = null!;

    /// <summary>当前全部子代理行 VM（跨组卡片聚合视图，供回放入口查找）</summary>
    public IReadOnlyList<AgentRunVm> AgentRuns => [.. _agentRunVms.Values];

    public ChatTurnProcessor(ObservableCollection<ChatUiMessage> messages)
        => _messages = messages ?? throw new ArgumentNullException(nameof(messages));

    /// <summary>
    /// 回合开始 — 追加流式中的助手占位；此后过程卡片经"插到占位之前"保持
    /// "过程在前、回复在后"的视觉顺序
    /// </summary>
    public void BeginTurn()
    {
        AssistantPlaceholder = new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        _messages.Add(AssistantPlaceholder);
        _assistantIndex = _messages.Count - 1;

        _agentTracker = new SubAgentRunTracker();
        _agentRunVms = new Dictionary<string, AgentRunVm>(StringComparer.Ordinal);
        _agentGroupCard = null;
        _currentThinking = null;
        _currentToolCall = null;
        _textBuilder.Clear();
        _thinkingBuilder.Clear();
        TotalTokens = 0;
    }

    /// <summary>
    /// 消费一条主对话流事件并映射到 UI 消息集合。
    /// 子代理事件（IsSubAgentActivity）优先路由到运行组卡片，防止污染主对话流。
    /// </summary>
    public void Process(ChatStreamEvent evt, bool streamingEnabled)
    {
        if (evt.IsSubAgentActivity)
        {
            HandleSubAgentActivity(evt);
            return;
        }

        switch (evt.Type)
        {
            case ChatStreamEventType.Content:
                if (evt.Content is not null)
                {
                    _textBuilder.Append(evt.Content);
                    if (streamingEnabled)
                        AssistantPlaceholder.Content = _textBuilder.ToString();
                }
                break;

            case ChatStreamEventType.Thinking:
                _thinkingBuilder.Append(evt.ThinkingContent);
                if (_currentThinking is null)
                {
                    _currentThinking = new ChatUiMessage
                    {
                        Role = MessageRole.Assistant,
                        Content = string.Empty,
                        Timestamp = DateTime.Now,
                        Kind = ChatUiMessageKind.Thinking
                    };
                    InsertBeforeAssistant(_currentThinking);
                }
                if (streamingEnabled)
                    _currentThinking.Content = _thinkingBuilder.ToString();
                break;

            case ChatStreamEventType.ToolCallStart:
                _currentToolCall = new ChatUiMessage
                {
                    Role = MessageRole.Assistant,
                    Content = string.Empty,
                    Timestamp = DateTime.Now,
                    Kind = ChatUiMessageKind.ToolCall,
                    ToolName = evt.ToolName,
                    ToolArguments = evt.ToolArguments,
                    ToolStartTime = DateTime.Now,
                    IsToolRunning = true
                };
                _currentToolCall.RefreshElapsed();
                InsertBeforeAssistant(_currentToolCall);
                break;

            case ChatStreamEventType.ToolProgress:
                if (_currentToolCall is not null && evt.ProgressMessage is not null)
                    _currentToolCall.Content = evt.ProgressMessage;
                break;

            case ChatStreamEventType.ToolCallEnd:
                if (_currentToolCall is not null)
                {
                    _currentToolCall.IsToolRunning = false;
                    _currentToolCall.RefreshElapsed();
                }
                InsertBeforeAssistant(new ChatUiMessage
                {
                    Role = MessageRole.Assistant,
                    Content = string.Empty,
                    Timestamp = DateTime.Now,
                    Kind = ChatUiMessageKind.ToolResult,
                    ToolName = evt.ToolName,
                    ToolResultText = evt.ToolResultText,
                    IsToolError = evt.IsToolError,
                    StructuredPatch = evt.StructuredPatch
                });
                _currentToolCall = null;
                break;

            case ChatStreamEventType.Complete:
                // G2 对齐 TUI：消费引擎上报的真实 token 用量（Done/Complete 事件携带）
                if (evt.Usage is not null)
                    TotalTokens += evt.Usage.TotalTokens;
                break;
        }
    }

    /// <summary>
    /// 回合收尾 — 最终一次性赋值：流式开启时为幂等收尾；关闭时这是唯一的内容填充点。
    /// 空思考气泡在此移除（关闭流式时思考内容也到此处才可见）。
    /// </summary>
    public void CompleteTurn(bool streamingEnabled)
    {
        if (_currentThinking is not null)
        {
            _currentThinking.Content = _thinkingBuilder.ToString();
            if (string.IsNullOrWhiteSpace(_currentThinking.Content))
                _messages.Remove(_currentThinking);
        }

        AssistantPlaceholder.Content = _textBuilder.ToString();
        AssistantPlaceholder.IsStreaming = false;
    }

    /// <summary>取消路径 — 停掉全部流式占位标记</summary>
    public void CancelTurn()
    {
        foreach (var m in _messages)
        {
            if (m.IsStreaming)
                m.IsStreaming = false;
        }
    }

    private void InsertBeforeAssistant(ChatUiMessage msg)
    {
        _messages.Insert(_assistantIndex, msg);
        _assistantIndex++;
    }

    /// <summary>
    /// 消费一条子代理事件 — 归约到 tracker，首次事件创建组卡片（插到助手占位之前，
    /// 整回合复用一张），随后同步行 VM 快照
    /// </summary>
    private void HandleSubAgentActivity(ChatStreamEvent evt)
    {
        _agentTracker.Observe(evt);

        if (_agentGroupCard is null)
        {
            _agentGroupCard = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = string.Empty,
                Timestamp = DateTime.Now,
                Kind = ChatUiMessageKind.AgentRunGroup,
                AgentRuns = []
            };
            InsertBeforeAssistant(_agentGroupCard);
        }

        foreach (var run in _agentTracker.Runs)
        {
            if (_agentRunVms.TryGetValue(run.AgentId, out var vm))
            {
                vm.Refresh();
            }
            else
            {
                vm = new AgentRunVm(run);
                _agentRunVms[run.AgentId] = vm;
                _agentGroupCard.AgentRuns!.Add(vm);
            }
        }
    }
}
