namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — 消息发送与子代理转发（F4/F5）。
/// 从 MainViewModel.cs 拆出以控制文件行数（关注点分离）。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>当前生成任务的取消源（停止生成用）</summary>
    private System.Threading.CancellationTokenSource? _sendCts;

    // === 多 subAgent 运行期显示（T4）— 组装逻辑已抽取到 ChatTurnProcessor ===

    /// <summary>本回合组装器（发送时创建；测试辅助路径懒创建）</summary>
    private ChatTurnProcessor? _turnProcessor;

    /// <summary>测试入口：重置子代理追踪状态</summary>
    internal void PrepareAgentRunTurnForTest()
        => _turnProcessor = new ChatTurnProcessor(Messages);

    /// <summary>测试入口：直接消费一条子代理事件（无回合占位的轻量路径）</summary>
    internal void HandleSubAgentActivityForTest(ChatStreamEvent evt)
        => GetOrCreateProcessor().Process(evt, streamingEnabled: false);

    private ChatTurnProcessor GetOrCreateProcessor() {
        if (_turnProcessor is not null)
            return _turnProcessor;
        _turnProcessor = new ChatTurnProcessor(Messages);
        _turnProcessor.BeginTurn();
        return _turnProcessor;
    }

    /// <summary>
    /// F4 规则1：@agentName 消息 → 查找运行中子代理并直接转发。
    /// 命中/未命中均以系统卡片回显；不开启新 LLM 回合
    /// </summary>
    private async Task HandleMentionAsync(string rawInput) {
        var parsed = JoinCode.Abstractions.Utils.SubAgentMentionParser.Parse(rawInput);
        if (parsed is null) {
            AddSystemMessage("⚠ @语法格式错误，正确格式: @agentName 消息内容（空格分隔）");
            return;
        }

        var (agentName, text) = parsed.Value;
        Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = rawInput, Timestamp = DateTime.Now });

        var agentId = await _session.FindSubAgentIdByNameAsync(agentName).ConfigureAwait(true);
        if (agentId is null) {
            var agents = await _session.GetBackgroundAgentsAsync().ConfigureAwait(true);
            var list = string.Join(", ", agents.Select(a => a.Name));
            AddSystemMessage($"⚠ 未找到子代理 @{agentName}，当前运行中: [{list}]");
            return;
        }

        var ok = await _session.ForwardInputToSubAgentAsync(agentId, text).ConfigureAwait(true);
        AddSystemMessage(ok ? $"📤 已转发给 @{agentName}" : $"⚠ 转发给 @{agentName} 失败");
    }

    /// <summary>
    /// F4 规则2：处理中收到普通消息且恰好一个运行中子代理 → 自动转发；
    /// 零个或多个时不转发（丢弃本次输入，保持原 IsBusy 忽略语义）
    /// </summary>
    private async Task TryAutoForwardToSingleAgentAsync(string message) {
        try {
            var agents = await _session.GetBackgroundAgentsAsync().ConfigureAwait(true);
            if (agents.Count != 1)
                return;

            await _session.ForwardInputToSubAgentAsync(agents[0].AgentId, message).ConfigureAwait(true);
            Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = message, Timestamp = DateTime.Now });
            AddSystemMessage($"📤 已转发给 @{agents[0].Name}");
        } catch (Exception ex) {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    private void AddSystemMessage(string content) {
        Messages.Add(new ChatUiMessage {
            Role = MessageRole.System,
            Content = content,
            Timestamp = DateTime.Now
        });
        StatusText = "就绪";
    }

    /// <summary>F5：打开子代理 worktree 目录 — 懒解析路径，未启用隔离时系统卡片提示</summary>
    [RelayCommand]
    private async Task OpenWorktreeInExplorerAsync(AgentRunVm? runVm) {
        if (runVm is null)
            return;

        var path = runVm.WorktreePath
            ?? await _session.GetSubAgentWorktreePathAsync(runVm.AgentId).ConfigureAwait(true);

        if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path)) {
            AddSystemMessage($"⚠ 子代理 {runVm.Run.Name} 未使用 worktree 隔离（无独立目录）");
            return;
        }

        runVm.SetWorktreePath(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
        AddSystemMessage($"📁 已打开 {runVm.Run.Name} 的工作树: {path}");
    }

    [RelayCommand]
    private async Task SendAsync() {
        var message = InputText;
        if (string.IsNullOrWhiteSpace(message))
            return;

        // ! / !! 前缀命令统一解析 — 由 PrefixCommandRouter.Parse 判断，不硬编码字符比较
        var prefixParsed = PrefixCommandRouter.Parse(message);

        // F4 规则1：@提及直发子代理 — 绕过主代理 LLM，不受 IsBusy 拦截（对齐 CLI ReplLoopStep）
        if (message[0] == '@') {
            InputText = string.Empty;
            await HandleMentionAsync(message).ConfigureAwait(false);
            return;
        }

        // !! 前缀命令 — 静默执行/打开，不触发 AI，不受 IsBusy 拦截（对齐 PI !! 设计）
        if (prefixParsed is { Prefix: "!!" }) {
            InputText = string.Empty;
            await HandleSilentPrefixCommandAsync(message).ConfigureAwait(false);
            return;
        }

        // F4 规则2：处理中且恰好一个运行中子代理 → 自动转发给它（对齐 CLI 单代理转发规则）
        if (IsBusy) {
            InputText = string.Empty;
            await TryAutoForwardToSingleAgentAsync(message).ConfigureAwait(false);
            return;
        }

        if (_inputHistory.Count == 0 || _inputHistory[^1] != message)
            _inputHistory.Add(message);
        _historyIndex = -1;

        InputText = string.Empty;
        IsBusy = true;
        StatusText = "思考中…";
        RunStatus.StartTurn();
        _sendCts = new System.Threading.CancellationTokenSource();
        OnPropertyChanged(nameof(CanStop));
        var stopReason = MarqueeStopReason.Normal;
        try {
            // ! 前缀命令路由 — 执行 shell 命令，输出注入 AI 上下文（对齐 PI ! 设计）
            if (prefixParsed is { Prefix: "!" }) {
                await HandleShellPrefixCommandAsync(message, _sendCts.Token).ConfigureAwait(false);
                StatusText = "就绪";
                return;
            }

            // 斜杠命令路由（G1 对齐 TUI）：/ 前缀走命令执行链路，不进聊天流
            if (message.StartsWith('/')) {
                Messages.Add(new ChatUiMessage {
                    Role = MessageRole.System,
                    Content = $"⚙️ {message}",
                    Timestamp = DateTime.Now
                });
                string output;
                try {
                    output = await _session.ExecuteSlashCommandAsync(message, _sendCts.Token).ConfigureAwait(false);
                } catch (Exception ex) {
                    output = $"命令执行失败: {ex.Message}";
                    ViewModelDiagnosticsLogger.WriteError(ex);
                }
                var commandEcho = Messages[^1];
                commandEcho.Content = string.IsNullOrWhiteSpace(output)
                    ? commandEcho.Content + "\n（无输出）"
                    : $"{commandEcho.Content}\n{output}";
                // T1：命令可能改变引擎上下文（/resume 装入历史、/clear 清空、/compact 压缩），
                // 重读引擎历史刷新消息列表，否则恢复的会话在界面不可见；命令回显保留在末尾
                await ReloadMessagesFromEngineAsync(commandEcho).ConfigureAwait(false);
                StatusText = "就绪";
                return;
            }

            // 应用编辑后的系统提示词（对齐 CLI --system-prompt：经 IChatService.SetSystemPromptAsync）
            if (!string.IsNullOrWhiteSpace(SystemPrompt)) {
                await _session.SetSystemPromptAsync(SystemPrompt, _sendCts.Token).ConfigureAwait(false);
            }

            Messages.Add(new ChatUiMessage {
                Role = MessageRole.User,
                Content = message,
                Timestamp = DateTime.Now
            });
            RenameActiveSessionTo(message);

            // 事件→消息组装委托给 ChatTurnProcessor（T7 抽取，可单测）
            _turnProcessor = new ChatTurnProcessor(Messages);
            _turnProcessor.BeginTurn();
            var processor = _turnProcessor;

            await foreach (var evt in _session.StreamAsync(message, _sendCts.Token)) {
                RunStatus.ReportActivity(
                    hasActiveTool: evt.Type == ChatStreamEventType.ToolCallStart,
                    label: evt.Type == ChatStreamEventType.ToolCallStart ? evt.ToolName : null);
                if (evt.Type == ChatStreamEventType.Complete && evt.Usage is not null)
                    RunStatus.AddTokens(evt.Usage.TotalTokens);
                processor.Process(evt, StreamingEnabled);
            }

            processor.CompleteTurn(StreamingEnabled);
            // 状态栏展示本轮真实 token 用量（引擎未上报时保留空串，不显示估算值）
            TokenUsageText = processor.TotalTokens > 0 ? $"Token:{processor.TotalTokens:N0}" : string.Empty;
            StatusText = "就绪";
        } catch (OperationCanceledException) {
            StatusText = "已停止生成";
            _turnProcessor?.CancelTurn();
            stopReason = MarqueeStopReason.UserAborted;
        } catch (Exception ex) {
            ErrorToastText = ex.Message;
            StatusText = "就绪";
            ViewModelDiagnosticsLogger.WriteError(ex);
            _turnProcessor?.CancelTurn();
            stopReason = MarqueeStopReason.Abnormal;
        } finally {
            _sendCts.Dispose();
            _sendCts = null;
            IsBusy = false;
            RunStatus.EndTurn(stopReason);
            OnPropertyChanged(nameof(CanStop));
            SaveActiveSession();
        }
    }
}