namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — 会话树形展示相关命令（需求11）。
/// 从 MainViewModel.cs 拆出以控制文件行数（JCC8001 ≤2000 行）。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>子代理中断后的空闲倒计时器 — null 表示无活跃倒计时</summary>
    private SubAgentIdleTimer? _idleTimer;

    /// <summary>60秒空闲超时触发 — mainAgent 接手编排（Cancel teammate + 提取 diff + 注入主会话触发 mainAgent）</summary>
#pragma warning disable JCC3005 // 事件处理器必须是 async void（EventHandler 签名）
    private async void OnMainAgentTakeoverRequested(object? sender, string teammateId)
#pragma warning restore JCC3005
    {
        try
        {
            // 1. 先获取 worktree 路径（Stop 之前 — Stop 之后无变更的 worktree 会被自动清理）
            var worktreePath = await _session.GetSubAgentWorktreePathAsync(teammateId).ConfigureAwait(false);

            // 2. 提取 worktree diff 摘要（对齐 PRD 4.6 — 供 mainAgent 分析接手）
            var diffSummary = string.Empty;
            if (!string.IsNullOrEmpty(worktreePath) && System.IO.Directory.Exists(worktreePath))
            {
                diffSummary = await ExtractWorktreeDiffSummaryAsync(worktreePath).ConfigureAwait(false);
            }

            // 3. 真正终止子代理（Stop 内部会调 CleanupWorktreeAsync — 有变更保留，无变更删除）
            var ok = await _session.StopBackgroundAgentAsync(teammateId).ConfigureAwait(false);

            // 4. 构造接手消息注入主会话触发 mainAgent（对齐 PRD 4.6）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                _idleTimer?.Dispose();
                _idleTimer = null;

                if (ok && !string.IsNullOrWhiteSpace(diffSummary))
                {
                    AddSystemMessage($"⏰ 子代理空闲超时，mainAgent 接手: {teammateId}");
                    var takeoverMessage = $"子代理 {teammateId} 已因空闲超时终止。它在 worktree 中改了以下内容，请接手分析并继续后续工作：\n\n```\n{diffSummary}\n```";
                    if (!IsBusy)
                    {
                        // 主会话空闲 — 自动注入并触发 mainAgent
                        InputText = takeoverMessage;
                        await SendCommand.ExecuteAsync(null);
                    }
                    else
                    {
                        // 主会话正忙 — 预填输入框，用户发送完后自动触发
                        InputText = takeoverMessage;
                        StatusText = $"子代理超时已终止，接手消息已预填（主会话忙，发送后触发）: {teammateId}";
                    }
                }
                else
                {
                    StatusText = ok
                        ? $"子代理空闲超时已终止（无变更）: {teammateId}"
                        : $"子代理超时终止失败: {teammateId}";
                }
                OnPropertyChanged(nameof(CanStop));
            });
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>提取 worktree 相对主仓库的 diff 摘要（git diff --stat）— 供 mainAgent 接手分析</summary>
    private static async Task<string> ExtractWorktreeDiffSummaryAsync(string worktreePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --stat",
                WorkingDirectory = worktreePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                return string.Empty;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync().ConfigureAwait(false);
            var output = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>需求11：子会话右键打开 worktree 文件夹 — 用资源管理器直达 worktree 目录</summary>
    [RelayCommand]
    private void OpenSessionWorktree(SessionItem? item)
    {
        if (item?.WorktreePath is not { Length: > 0 } path || !System.IO.Directory.Exists(path))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    /// <summary>需求11：切换主会话展开/折叠（显示子会话列表）</summary>
    [RelayCommand]
    private void ToggleSessionExpanded(SessionItem? item)
    {
        if (item is not null)
            item.IsExpanded = !item.IsExpanded;
    }

    /// <summary>需求11：加载子会话内容到消息区 — SubSessionMessages 缓存优先，否则从引擎获取</summary>
    public async Task LoadSubSessionContentAsync(SessionItem session)
    {
        Messages.Clear();
        if (session.SubSessionMessages is { Count: > 0 } subMsgs)
        {
            foreach (var m in subMsgs)
                Messages.Add(m);
            return;
        }
        try
        {
            var records = await _session.GetMessagesAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var r in records)
            {
                if (!string.IsNullOrWhiteSpace(r.Content))
                    Messages.Add(new ChatUiMessage { Role = MessageRoleExtensions.FromValue(r.Role) ?? MessageRole.User, Content = r.Content, Timestamp = r.Timestamp });
            }
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>
    /// 新增需求：终止当前聚焦的子代理会话（fire-and-forget，避免阻塞 UI 线程）。
    /// 双击 ESC 时若当前视图为子会话，仅终止该 subAgent，不影响主会话与其他子代理；遥测网络不终止。
    /// </summary>
    public async Task StopSubAgentAsync(SessionItem subSession)
    {
        try
        {
            var ok = await _session.StopBackgroundAgentAsync(subSession.Id).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ok)
                    subSession.SubSessionState = "Cancelled";
                StatusText = ok ? $"已终止子代理: {subSession.Title}" : $"终止子代理失败: {subSession.Title}";
                OnPropertyChanged(nameof(CanStop));
            });
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>用户打字（KeyDown，未发送）重置空闲倒计时 — 用户还在活动，别移交 mainAgent</summary>
    public void ResetIdleTimer() => _idleTimer?.Reset();

    /// <summary>用户发送消息取消空闲倒计时 — 用户主动接管，立即恢复子代理（不等倒计时）</summary>
    public void StopIdleTimer()
    {
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    /// <summary>
    /// 中断子代理当前 work（非终止）— teammate 进 idle 等 next prompt，可恢复。
    /// 双击 ESC 走此路径（对齐 TS 原版 inProcessRunner ESC 行为）。
    /// </summary>
    public async Task InterruptSubAgentAsync(SessionItem subSession)
    {
        try
        {
            var ok = await _session.InterruptSubAgentAsync(subSession.Id).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ok)
                {
                    subSession.SubSessionState = "Interrupted";
                    // 启动60秒空闲倒计时 — 用户打字重置，发送取消，超时移交 mainAgent
                    _idleTimer?.Dispose();
                    _idleTimer = new SubAgentIdleTimer(60, remaining =>
                    {
                        StatusText = $"子代理已中断 · {remaining}s内无输入将移交mainAgent";
                    });
                    _idleTimer.MainAgentTakeoverRequested += OnMainAgentTakeoverRequested;
                    _idleTimer.Start(subSession.Id);
                }
                else
                {
                    StatusText = $"中断子代理失败: {subSession.Title}";
                }
                OnPropertyChanged(nameof(CanStop));
            });
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>
    /// 是否可停止当前生成 — 主会话发送中 或 当前聚焦子会话运行中（新增需求：双击ESC只打断当前视图会话）。
    /// 遥测网络不受影响（独立服务）。
    /// </summary>
    public bool CanStop => (_sendCts is not null && !_sendCts.IsCancellationRequested)
        || (_activeSession is { IsSubSession: true, IsSubSessionRunning: true });

    /// <summary>
    /// 停止当前生成 — 双击ESC只打断当前聚焦的会话标签页。
    /// 当前聚焦子会话且运行中 → 中断该子代理（Interrupt，进 idle 可恢复，非终止）；
    /// 当前聚焦主会话 → 取消主会话发送 CTS（遥测不终止）。
    /// </summary>
    [RelayCommand]
    private void StopGenerating()
    {
        if (_activeSession is { IsSubSession: true, IsSubSessionRunning: true } subSession)
        {
            _ = InterruptSubAgentAsync(subSession);
            return;
        }
        if (_sendCts is not null)
        {
            _sendCts.Cancel();
            OnPropertyChanged(nameof(CanStop));
        }
    }

    /// <summary>需求11：异步从引擎拉取子会话填充 Children — AttachRealSession 后调用（快照避免跨线程）</summary>
    public async Task PopulateSubSessionsAsync(SessionItem[] sessions)
    {
        try
        {
            foreach (var session in sessions)
            {
                var subs = await _session.GetSubSessionsAsync(session.Id).ConfigureAwait(false);
                if (subs.Count == 0)
                    continue;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    session.Children.Clear();
                    foreach (var sub in subs)
                    {
                        session.Children.Add(new SessionItem
                        {
                            Id = sub.Id,
                            Title = sub.Title,
                            ParentId = sub.ParentSessionId,
                            HasWorktree = sub.WorktreePath is not null,
                            WorktreePath = sub.WorktreePath,
                            SubSessionState = sub.State
                        });
                    }
                    session.IsExpanded = true;
                });
            }
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }
}
