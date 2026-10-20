namespace JoinCode.Gui.WebView;

/// <summary>
/// WebUI 会话流投影层 — 把 <see cref="IJccChatSession.StreamAsync"/> 事件序列投影为桥接消息：
/// Content → chat/delta（逐块透传）；循环结束/异常/用户停止/取消均以恰好一条 chat/done 收尾
/// （web 侧据此恢复发送按钮；<see cref="ChatDoneMessage.Stopped"/> 区分用户主动停止）。
/// v1 仅透传正文（Thinking/工具事件留待 P6-3 消息区）；并发发送与引擎未就绪均回 app/error 诱导（fail-closed，不抛）。
/// </summary>
internal sealed class WebChatFlow {
    private readonly IJccChatSession _session;
    private bool _streaming;
    private CancellationTokenSource? _turnCts;
    private volatile bool _stopRequested;

    /// <summary>构造会话流投影层</summary>
    /// <param name="session">引擎会话门面（真实 JccChatSession 或测试桩）</param>
    public WebChatFlow(IJccChatSession session) {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <summary>当前是否有一轮回复进行中</summary>
    public bool IsStreaming => _streaming;

    /// <summary>
    /// 请求停止本轮生成（对齐发送按钮 → 停止按钮语义）：取消在途流，已产出增量保留。
    /// 空闲态调用为 no-op；停止原因以 chat/done 的 Stopped=true 表达，不回 app/error（非故障语义）。
    /// </summary>
    public void Stop() {
        var turn = _turnCts;
        if (!_streaming || turn is null) {
            return;
        }
        _stopRequested = true;
        turn.Cancel();
    }

    /// <summary>发送提示词并把流式事件投影为桥接消息（emit 同步回调；拒绝/异常经 app/error 表达）</summary>
    /// <param name="prompt">提示词全文</param>
    /// <param name="emit">桥接消息出口（宿主注入 PostWebMessageAsJson 封装）</param>
    /// <param name="cancellationToken">取消令牌（窗口关闭/看门狗）</param>
    public async Task SendAsync(string prompt, Action<BridgeMessage> emit, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(emit);
        if (_streaming) {
            emit(new AppErrorMessage("上一轮回复仍在进行中", "等待本轮回复结束（chat/done）后再发送"));
            return;
        }
        if (!_session.IsReady) {
            emit(new AppErrorMessage("引擎尚未就绪", "等待启动完成后重试；若长时间未就绪查看 dumps/startup_timing.log"));
            return;
        }
        _streaming = true;
        _stopRequested = false;
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _turnCts = turnCts;
        try {
            await foreach (var evt in _session.StreamAsync(prompt, turnCts.Token)) {
                if (evt.Type is ChatStreamEventType.Content && !string.IsNullOrEmpty(evt.Content)) {
                    emit(new ChatDeltaMessage(evt.Content));
                }
            }
        } catch (OperationCanceledException) when (_stopRequested) {
            // 用户主动停止：非故障语义，静默收尾（done.Stopped=true 表达）
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // 令牌未取消仍抛 OCE = 引擎内部超时/中断（HttpClient 超时同型），必须回错误而非静默
            emit(new AppErrorMessage("响应超时或被中断", "检查网络与模型配置后重试；已输出内容已保留"));
        } catch (OperationCanceledException) {
            // 窗口关闭：不回错误提示，chat/done 由 finally 统一收尾
        } catch (Exception ex) {
            emit(new AppErrorMessage($"发送失败：{ex.Message}", "检查模型配置与网络后重试；已输出内容已保留"));
        } finally {
            _turnCts = null;
            _streaming = false;
            emit(new ChatDoneMessage(_stopRequested));
        }
    }
}
