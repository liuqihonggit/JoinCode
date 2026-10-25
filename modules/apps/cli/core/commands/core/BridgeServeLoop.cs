namespace JoinCode.CliCommands;

/// <summary>
/// bridge 服务循环 — jcc bridge_serve 子命令核心（ADR 0141）：
    /// stdin 逐行读 BridgeRequest，stdout 逐行写 BridgeEvent；回合任务与读循环并发
    /// （interrupt 可随时到达）。submit → turn_started + 回合执行 + turn_completed；
    /// interrupt → 取消回合 → turn_interrupted；忙时 submit → error 诱导提示；
    /// set_permission_mode → 权限应用器（解析+闸门）→ permission_mode_changed 回执（实际生效值）；
    /// stop_task → 后台任务泵停止分派（M8-8 后续 — cancelled 终态 upsert 回推）；
    /// 未知 type → stderr 诊断不中断；quit/Eof → 取消在途回合收尾后退出。
/// 回合内部事件（thinking/tool/token/assistant_message）由 BridgeEventConsumer 实时推送，
/// 回合生命周期事件（started/completed/interrupted/error）由本循环统一发出。
/// stdout 写出经 TextWriter.Synchronized + 单次 Write(json+"\n") 保证行原子性（协议统一 LF）。
/// </summary>
internal static class BridgeServeLoop {
    /// <summary>回合执行器 — 生产侧绑定 SessionController.StreamResponseAsync，测试侧注入假执行器模拟 LLM 流。</summary>
    /// <param name="input">用户提交文本</param>
    /// <param name="consumer">回合事件消费器（executor 内触发回调实时推送事件行）</param>
    /// <param name="ct">回合取消令牌（interrupt 触发）</param>
    public delegate Task TurnExecutor(string input, IEventConsumer consumer, CancellationToken ct);

    /// <summary>
    /// 权限模式应用器 — 解析协议字符串并应用到引擎权限系统，返回实际生效模式；
    /// 未知值 / 引擎未提供权限系统返回 null（loop 发 error 诱导事件）。
    /// </summary>
    /// <param name="mode">协议字符串（plan/auto/ask/bypass/unattended）</param>
    public delegate PermissionMode? PermissionModeApplier(string mode);

    /// <summary>合法权限模式值清单（error 诱导提示用，与 abs_guard [EnumValue] 一致）。</summary>
    private const string ValidPermissionModes = "plan, auto, ask, bypass, unattended";

    /// <summary>支持的退出码 — 正常退出（quit/Eof/外部取消均收尾后返回 0）。</summary>
    private const int ExitOk = 0;

    /// <summary>运行协议主循环直到 quit/Eof/外部取消（applyPermissionMode 为 null 时 set_permission_mode 回 error 诱导提示）。
    /// backgroundPump 可选 — 组装层（BridgeServeCommand）注入时：consumer 构造后 Bind+Start（订阅后台 fork/teammate
    /// 完成事件直推 background_task 行），每回合终态后 PublishSnapshotAsync（TUI 面板与引擎状态对齐），循环退出前 StopAsync。</summary>
    public static async Task<int> RunAsync(
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        TurnExecutor executeTurn,
        CancellationToken ct,
        PermissionModeApplier? applyPermissionMode = null,
        BackgroundTaskEventPump? backgroundPump = null) {
        var synced = TextWriter.Synchronized(stdout);
        var consumer = new BridgeEventConsumer(synced);
        if (backgroundPump is not null) {
            backgroundPump.Bind(consumer);
            await backgroundPump.StartAsync().ConfigureAwait(false);
        }
        CancellationTokenSource? turnCts = null;
        Task? turnTask = null;
        var exit = false;
        ulong? lastProcessedSeq = null;

        while (!exit && !ct.IsCancellationRequested) {
            var result = Cli.Bridge.BridgeProtocol.TryReadRequest(stdin, out var request, out var diagnostic);
            switch (result) {
                case Cli.Bridge.BridgeProtocol.ReadResult.Eof:
                    exit = true;
                    break;
                case Cli.Bridge.BridgeProtocol.ReadResult.Skipped:
                    stderr.WriteLine(diagnostic);
                    break;
                default:
                    if (request is not null) {
                        if (request is Cli.Bridge.BridgeRequest.Submit { Seq: { } dupSeq } && dupSeq == lastProcessedSeq) {
                            WriteEvent(synced, new Cli.Bridge.BridgeEvent.Error("重复提交(seq 已处理)，已忽略。") { AckSeq = dupSeq });
                            WriteEvent(synced, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = dupSeq });
                            break;
                        }
                        if (request is Cli.Bridge.BridgeRequest.Submit { Seq: { } newSeq })
                            lastProcessedSeq = newSeq;
                        (exit, turnCts, turnTask) = DispatchRequest(request, synced, consumer, executeTurn, applyPermissionMode, backgroundPump, turnCts, turnTask);
                    }
                    break;
            }
        }

        turnCts?.Cancel();
        if (turnTask is not null)
            await turnTask.ConfigureAwait(false);
        turnCts?.Dispose();
        if (backgroundPump is not null)
            await backgroundPump.StopAsync().ConfigureAwait(false);
        return ExitOk;
    }

    /// <summary>分派单条请求：submit 起新回合（忙时拒绝；带图片附件经
    /// <see cref="ProjectImageAttachments"/> 降级投影）/ interrupt 取消 / set_permission_mode 权限应用 / quit 退出。</summary>
    private static (bool Exit, CancellationTokenSource? TurnCts, Task? TurnTask) DispatchRequest(
        Cli.Bridge.BridgeRequest request,
        TextWriter stdout,
        BridgeEventConsumer consumer,
        TurnExecutor executeTurn,
        PermissionModeApplier? applyPermissionMode,
        BackgroundTaskEventPump? backgroundPump,
        CancellationTokenSource? turnCts,
        Task? turnTask) {
        switch (request) {
            case Cli.Bridge.BridgeRequest.Submit(var text, var images):
                if (turnTask is { IsCompleted: false }) {
                    WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error(
                        "回合进行中，本次提交已忽略。如需提交新输入，请先发送 interrupt 打断当前回合。") { AckSeq = request.Seq });
                    return (false, turnCts, turnTask);
                }

                turnCts?.Dispose();
                var newCts = new CancellationTokenSource();
                var newTask = RunTurnAsync(stdout, consumer, executeTurn, backgroundPump,
                    ProjectImageAttachments(text, images), newCts.Token, request.Seq);
                return (false, newCts, newTask);
            case Cli.Bridge.BridgeRequest.Interrupt:
                turnCts?.Cancel();
                return (false, turnCts, turnTask);
            case Cli.Bridge.BridgeRequest.SetPermissionMode(var mode):
                ApplyPermissionMode(stdout, applyPermissionMode, mode, request.Seq);
                return (false, turnCts, turnTask);
            case Cli.Bridge.BridgeRequest.StopTask(var kind, var taskId):
                // M8-8 后续 — 停止后台任务分派（fire-and-forget：泵内部吞异常降级 + cancelled 终态
                // upsert 经 consumer 回推 background_task 行，请求读循环不等待 — 对齐 interrupt 即返形态）；
                // ct=None 有意 — 停止分派与宿主回合生命周期解耦（ServeLoop 退出时泵经 StopAsync 统一收尾，
                // 取消引擎调用反可能漏发 cancelled 终态 upsert 致 TUI 面板条目滞留）；
                // 泵未注入（引擎无后台能力 — 面板无条目，请求实际不可达）静默跳过
                if (backgroundPump is not null)
                    _ = backgroundPump.StopTaskAsync(kind, taskId, CancellationToken.None);
                return (false, turnCts, turnTask);
            case Cli.Bridge.BridgeRequest.Quit:
                return (true, turnCts, turnTask);
            default:
                return (false, turnCts, turnTask);
        }
    }

    /// <summary>
    /// 应用权限模式：成功 → permission_mode_changed 回执（实际生效值 — 安全闸拦截时与请求值不同）；
    /// 未知值 / 引擎未提供权限系统 → error 诱导事件（合法值清单 + bypass 安全闸说明）。
    /// </summary>
    private static void ApplyPermissionMode(TextWriter stdout, PermissionModeApplier? applyPermissionMode, string mode, ulong? seq) {
        var applied = applyPermissionMode?.Invoke(mode);
        if (applied is { } actual) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.PermissionModeChanged(actual.ToValue()) { AckSeq = seq });
            return;
        }

        WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error(
            $"权限模式 '{mode}' 无法应用（未知值或引擎未提供权限系统）。合法值：{ValidPermissionModes}。bypass 受 settings.json 的 disableBypassPermissionsMode 安全闸管控，被拦截时保持原模式。") { AckSeq = seq });
    }

    /// <summary>
    /// 附件投影为回合输入（G4 — 与进程内直连版 EngineTurnClient.ProjectImageAttachments 同一降级语义）：
    /// 引擎回合链 string-only（SessionController.StreamResponseAsync(string) → AddUserMessageAsync(string)
    /// 用户消息无 image content block 通道），图片降级为文本引用段附加到回合输入（模型感知图片存在 +
    /// 元数据；base64 全量内嵌会以数十万 token 计击穿上下文上限，不可用）。
    /// 形态：每附件一行 [Image #N: filename (mediaType)]，空文本兜底纯投影段（附件即提交内容）。
    /// TODO（引擎用户消息 ContentBlocks 通道）：AddUserMessageAsync 支持图片块后升级真实多模态提交
    /// （基准 Image refs become content blocks 完整语义）。
    /// </summary>
    /// <param name="input">用户提交文本（占位已由 TUI 侧 shell 剥离 — 剥离后空文本由投影段兜底非空）。</param>
    /// <param name="images">图片附件（null/空 = 原文透传零改动）。</param>
    /// <returns>投影后回合输入。</returns>
    private static string ProjectImageAttachments(string input, IReadOnlyList<Cli.Bridge.BridgeImageAttachment>? images) {
        if (images is not { Count: > 0 })
            return input;

        var summary = string.Join("\n", images.Select(static (image, i) =>
            $"[Image #{i + 1}: {image.Filename} ({image.MediaType})]"));
        return input.Length == 0 ? summary : $"{input}\n\n{summary}";
    }

    /// <summary>执行一个回合：turn_started → executor（事件实时推送）→ completed/interrupted/error
    /// → 后台任务快照全量 upsert（回合结束边界 — idle 期 TUI 面板与引擎后台任务状态对齐；
    /// 泵内部吞查询异常，快照失败不杀回合收尾）。</summary>
    private static async Task RunTurnAsync(
        TextWriter stdout,
        BridgeEventConsumer consumer,
        TurnExecutor executeTurn,
        BackgroundTaskEventPump? backgroundPump,
        string input,
        CancellationToken ct,
        ulong? seq) {
        WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnStarted() { AckSeq = seq });
        consumer.Reset();
        consumer.SetAckSeq(seq);
        try {
            await executeTurn(input, consumer, ct).ConfigureAwait(false);
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = seq });
        } catch (OperationCanceledException) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnInterrupted() { AckSeq = seq });
        } catch (Exception ex) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error($"回合执行失败：{ex.Message}") { AckSeq = seq });
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = seq });
        } finally {
            if (backgroundPump is not null)
                await backgroundPump.PublishSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>写一条事件行（单次 Write 保证行原子性 + LF + Flush，协议统一 \n）。</summary>
    private static void WriteEvent(TextWriter stdout, Cli.Bridge.BridgeEvent bridgeEvent) {
        stdout.Write(Cli.Bridge.BridgeProtocol.SerializeEvent(bridgeEvent) + "\n");
        stdout.Flush();
    }
}
