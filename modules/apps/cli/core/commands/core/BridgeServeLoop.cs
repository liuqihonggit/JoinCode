namespace JoinCode.CliCommands;

/// <summary>
/// bridge 服务循环 — jcc bridge_serve 子命令核心（ADR 0141）：
/// stdin 逐行读 BridgeRequest，stdout 逐行写 BridgeEvent；回合任务与读循环并发
/// （interrupt 可随时到达）。
/// <para>
/// ADR 0165 阶段 2 / TASK136 — **跨进程多查询**：提交不再因忙被拒，每条查询独立取消源 +
/// 独立事件消费器，事件按 queryId 归因（对照进程内宿主：每查询新建 SessionController + consumer）。
/// 唯一仍拒绝的情形是同 queryId 并发复用（身份冲突会让两条查询的事件无法区分）。
/// </para>
/// <para>
/// 协议面：submit → turn_started + 回合执行 + turn_completed；interrupt{queryId} → 定向取消该查询
/// （无 queryId 的旧客户端语义 = 取消全部在途）；set_permission_mode → 权限应用器（解析+闸门）→
/// permission_mode_changed 回执（实际生效值）；stop_task → 后台任务泵停止分派（M8-8 后续 —
/// cancelled 终态 upsert 回推）；未知 type → stderr 诊断不中断；quit/Eof → 取消全部在途查询收尾后退出。
/// </para>
/// <para>
/// 回合内部事件（thinking/tool/token/assistant_message）由 BridgeEventConsumer 实时推送，
/// 回合生命周期事件（started/completed/interrupted/error）由本循环统一发出。
/// stdout 写出经 TextWriter.Synchronized + 单次 Write(json+"\n") 保证行原子性（协议统一 LF）。
/// </para>
/// </summary>
internal static class BridgeServeLoop {
    /// <summary>回合执行器 — 生产侧绑定 SessionController.StreamResponseAsync（每查询新建控制器），
    /// 测试侧注入假执行器模拟 LLM 流。</summary>
    /// <param name="input">用户提交文本</param>
    /// <param name="consumer">该查询专属的事件消费器（executor 内触发回调实时推送事件行）</param>
    /// <param name="ct">该查询的取消令牌（interrupt 触发）</param>
    public delegate Task TurnExecutor(string input, IEventConsumer consumer, CancellationToken ct);

    /// <summary>
    /// 权限模式应用器 — 解析协议字符串并应用到引擎权限系统，返回实际生效模式；
    /// 未知值 / 引擎未提供权限系统返回 null（loop 发 error 诱导事件）。
    /// </summary>
    /// <param name="mode">协议字符串（plan/auto/ask/bypass/unattended）</param>
    public delegate PermissionMode? PermissionModeApplier(string mode);

    /// <summary>支持的退出码 — 正常退出（quit/Eof/外部取消均收尾后返回 0）。</summary>
    private const int ExitOk = 0;

    /// <summary>
    /// 在途查询槽（TASK136）— 每查询一份取消源与任务句柄。
    /// <para>取消与释放的竞态：查询收尾后线程池侧释放取消源，读循环侧可能正好在取消它 ——
    /// <see cref="Cancel"/> 吞掉该竞态（已收尾的查询本就不需要再取消），
    /// 避免迟到的 interrupt 把服务循环打崩。</para>
    /// </summary>
    private sealed class TurnRun {
        /// <summary>构造在途查询槽。</summary>
        /// <param name="queryId">查询身份（字典键，与之一致）。</param>
        /// <param name="cts">该查询的取消源（所有权转移给本类）。</param>
        public TurnRun(string queryId, CancellationTokenSource cts) {
            QueryId = queryId;
            Cts = cts;
        }

        /// <summary>查询身份。</summary>
        public string QueryId { get; }

        /// <summary>该查询的取消源。</summary>
        public CancellationTokenSource Cts { get; }

        /// <summary>该查询的回合任务（注册后赋值；未赋值视为已完成）。</summary>
        public Task Task { get; set; } = Task.CompletedTask;

        /// <summary>取消该查询 — 已收尾并释放时静默忽略。</summary>
        /// <returns>是否真的发出取消。</returns>
        public bool Cancel() {
            try {
                Cts.Cancel();
                return true;
            } catch (ObjectDisposedException) {
                // 查询恰好在这一刻收尾并释放了取消源 —— 无需取消
                return false;
            }
        }

        /// <summary>收尾 — 释放取消源（幂等，仅由该查询自己的任务调用）。</summary>
        public void Close() => Cts.Dispose();
    }

    /// <summary>运行协议主循环直到 quit/Eof/外部取消（applyPermissionMode 为 null 时 set_permission_mode 回 error 诱导提示）。
    /// backgroundPump 可选 — 组装层（BridgeServeCommand）注入时：会话级消费器 Bind+Start（订阅后台 fork/teammate
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
        // 会话级事件（background_task）走独立消费器：并发回合下「借用某个回合的 ackSeq」无定义，
        // 且 background_task 本就不打 queryId（ADR 0165）
        var backgroundConsumer = new BridgeEventConsumer(synced);
        if (backgroundPump is not null) {
            backgroundPump.Bind(backgroundConsumer);
            await backgroundPump.StartAsync().ConfigureAwait(false);
        }

        var runs = new ConcurrentDictionary<string, TurnRun>(StringComparer.Ordinal);
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
                            WriteEvent(synced, new Cli.Bridge.BridgeEvent.Error("重复提交(seq 已处理)，已忽略。") { AckSeq = dupSeq, QueryId = request.QueryId });
                            WriteEvent(synced, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = dupSeq, QueryId = request.QueryId });
                            break;
                        }
                        if (request is Cli.Bridge.BridgeRequest.Submit { Seq: { } newSeq })
                            lastProcessedSeq = newSeq;
                        exit = DispatchRequest(request, synced, runs, executeTurn, applyPermissionMode, backgroundPump);
                    }
                    break;
            }
        }

        await CancelAllQueriesAsync(runs).ConfigureAwait(false);
        if (backgroundPump is not null)
            await backgroundPump.StopAsync().ConfigureAwait(false);
        return ExitOk;
    }

    /// <summary>取消并等待全部在途查询收尾（quit/Eof/外部取消共用出口 — 顺序不承诺）。</summary>
    /// <param name="runs">在途查询表（回合任务各自把终态翻译为事件，此处不会抛）。</param>
    private static async Task CancelAllQueriesAsync(ConcurrentDictionary<string, TurnRun> runs) {
        foreach (var run in runs.Values)
            run.Cancel();

        var pending = runs.Values.Select(static run => run.Task).ToArray();
        if (pending.Length > 0)
            await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>分派单条请求：submit 起新查询（仅拒绝同 queryId 复用；带图片附件经
    /// <see cref="ProjectImageAttachments"/> 降级投影）/ interrupt 定向取消 / set_permission_mode 权限应用 / quit 退出。</summary>
    /// <returns>是否要求主循环退出。</returns>
    private static bool DispatchRequest(
        Cli.Bridge.BridgeRequest request,
        TextWriter stdout,
        ConcurrentDictionary<string, TurnRun> runs,
        TurnExecutor executeTurn,
        PermissionModeApplier? applyPermissionMode,
        BackgroundTaskEventPump? backgroundPump) {
        switch (request) {
            case Cli.Bridge.BridgeRequest.Submit(var text, var images): {
                // ADR 0165：旧请求无 queryId 时生成一个，保证本查询所有事件归因一致
                var queryId = string.IsNullOrWhiteSpace(request.QueryId)
                    ? $"q-{Guid.NewGuid():N}"
                    : request.QueryId;
                var run = new TurnRun(queryId, new CancellationTokenSource());
                if (!runs.TryAdd(queryId, run)) {
                    WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error(
                        $"同一 queryId（{queryId}）的查询已在执行，本次提交已忽略。请先对该查询发送 interrupt，或换一个新 queryId 再提交。") { AckSeq = request.Seq, QueryId = queryId });
                    return false;
                }

                // 每查询独立消费器：_fullResponse/_thinking 等回合内状态不跨查询串台
                var consumer = new BridgeEventConsumer(stdout);
                run.Task = RunTurnAsync(stdout, runs, run, consumer, executeTurn, backgroundPump,
                    ProjectImageAttachments(text, images), request.Seq);
                return false;
            }
            case Cli.Bridge.BridgeRequest.Interrupt:
                // ADR 0165：定向取消 — 带 queryId 时只取消该查询；未命中视为过期/他人的打断（迟到的 Esc
                // 不得误杀新提交）。无 queryId 是旧客户端语义：取消全部在途。
                if (request.QueryId is { } target) {
                    if (runs.TryGetValue(target, out var targeted))
                        targeted.Cancel();
                    return false;
                }
                foreach (var run in runs.Values)
                    run.Cancel();
                return false;
            case Cli.Bridge.BridgeRequest.SetPermissionMode(var mode):
                ApplyPermissionMode(stdout, applyPermissionMode, mode, request.Seq);
                return false;
            case Cli.Bridge.BridgeRequest.StopTask(var kind, var taskId):
                // M8-8 后续 — 停止后台任务分派（fire-and-forget：泵内部吞异常降级 + cancelled 终态
                // upsert 经 consumer 回推 background_task 行，请求读循环不等待 — 对齐 interrupt 即返形态）；
                // ct=None 有意 — 停止分派与宿主回合生命周期解耦（ServeLoop 退出时泵经 StopAsync 统一收尾，
                // 取消引擎调用反可能漏发 cancelled 终态 upsert 致 TUI 面板条目滞留）；
                // 泵未注入（引擎无后台能力 — 面板无条目，请求实际不可达）静默跳过
                if (backgroundPump is not null)
                    _ = backgroundPump.StopTaskAsync(kind, taskId, CancellationToken.None);
                return false;
            case Cli.Bridge.BridgeRequest.Quit:
                return true;
            default:
                return false;
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
            PermissionModeApplierFactory.DescribeFailure(mode)) { AckSeq = seq });
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

    /// <summary>执行一条查询的回合：turn_started → executor（事件实时推送）→ completed/interrupted/error
    /// → 自摘除在途槽 → 后台任务快照全量 upsert（回合结束边界 — idle 期 TUI 面板与引擎后台任务状态对齐；
    /// 泵内部吞查询异常，快照失败不杀回合收尾）。</summary>
    private static async Task RunTurnAsync(
        TextWriter stdout,
        ConcurrentDictionary<string, TurnRun> runs,
        TurnRun run,
        BridgeEventConsumer consumer,
        TurnExecutor executeTurn,
        BackgroundTaskEventPump? backgroundPump,
        string input,
        ulong? seq) {
        var queryId = run.QueryId;
        WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnStarted() { AckSeq = seq, QueryId = queryId });
        consumer.SetAckSeq(seq);
        consumer.SetQueryId(queryId);
        try {
            await executeTurn(input, consumer, run.Cts.Token).ConfigureAwait(false);
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = seq, QueryId = queryId });
        } catch (OperationCanceledException) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnInterrupted() { AckSeq = seq, QueryId = queryId });
        } catch (Exception ex) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error($"回合执行失败：{ex.Message}") { AckSeq = seq, QueryId = queryId });
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnCompleted() { AckSeq = seq, QueryId = queryId });
        } finally {
            // 先摘除再释放：读循环侧可能在 Cancel 与 Close 之间掠过（TurnRun.Cancel 吞该竞态）
            runs.TryRemove(queryId, out _);
            run.Close();
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
