namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Diag, Description = "查看崩溃快照和诊断信息", Usage = "/diag [recent|fence <name>|ack <id>|clear]", Category = ChatCommandCategory.System, IsHidden = true)]
public sealed class DiagCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var store = context.Services.GetService<ICrashSnapshotStore>();
        if (store is null)
        {
            TerminalHelper.WriteLine("CrashSnapshotStore 未初始化 — 诊断功能不可用");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var args = GetNormalizedArgs(context);
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts[0] is "recent" or "")
        {
            ShowRecent(store);
        }
        else if (parts[0] is "fence" && parts.Length > 1)
        {
            ShowByFence(store, parts[1]);
        }
        else if (parts[0] is "ack" && parts.Length > 1)
        {
            Acknowledge(store, parts[1]);
        }
        else if (parts[0] is "detail" && parts.Length > 1)
        {
            ShowDetail(store, parts[1]);
        }
        else
        {
            TerminalHelper.WriteLine("用法: /diag [recent|fence <name>|ack <id>|detail <id>]");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void ShowRecent(ICrashSnapshotStore store)
    {
        var report = ((CrashSnapshotStore)store).FormatReport(20);
        TerminalHelper.WriteLine(report);
    }

    private static void ShowByFence(ICrashSnapshotStore store, string fenceName)
    {
        var snapshots = store.GetByFence(fenceName);
        if (snapshots.Count == 0)
        {
            TerminalHelper.WriteLine($"围栏 '{fenceName}' 无崩溃记录");
            return;
        }

        TerminalHelper.WriteLine($"围栏 '{fenceName}' 崩溃记录 ({snapshots.Count} 条):");
        foreach (var s in snapshots.Take(20))
        {
            TerminalHelper.WriteLine($"  [{s.Severity.ToValue()}] {s.ExceptionType}: {s.ExceptionMessage}");
        }
    }

    private static void Acknowledge(ICrashSnapshotStore store, string idText)
    {
        if (!Guid.TryParse(idText, out var id))
        {
            TerminalHelper.WriteLine($"无效的快照 ID: {idText}");
            return;
        }

        var snapshot = store.GetById(id);
        if (snapshot is null)
        {
            TerminalHelper.WriteLine($"快照 {id:N} 不存在");
            return;
        }

        store.Acknowledge(id);
        TerminalHelper.WriteLine($"快照 {id:N} 已确认");
    }

    private static void ShowDetail(ICrashSnapshotStore store, string idText)
    {
        if (!Guid.TryParse(idText, out var id))
        {
            TerminalHelper.WriteLine($"无效的快照 ID: {idText}");
            return;
        }

        var snapshot = store.GetById(id);
        if (snapshot is null)
        {
            TerminalHelper.WriteLine($"快照 {id:N} 不存在");
            return;
        }

        TerminalHelper.WriteLine($"═══ 崩溃快照 {snapshot.Id:N} ═══");
        TerminalHelper.WriteLine($"围栏:   {snapshot.FenceName}");
        TerminalHelper.WriteLine($"严重度: {snapshot.Severity.ToValue()}");
        TerminalHelper.WriteLine($"状态:   {snapshot.State}");
        TerminalHelper.WriteLine($"时间:   {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss.fff}");
        TerminalHelper.WriteLine();

        TerminalHelper.WriteLine($"异常类型: {snapshot.ExceptionType}");
        TerminalHelper.WriteLine($"异常消息: {snapshot.ExceptionMessage}");
        if (snapshot.ErrorCode is not null)
            TerminalHelper.WriteLine($"错误码:   {snapshot.ErrorCode}");
        if (snapshot.ErrorCategory is not null)
            TerminalHelper.WriteLine($"错误类别: {snapshot.ErrorCategory}");
        TerminalHelper.WriteLine();

        if (snapshot.ExceptionChain.Depth > 1)
        {
            TerminalHelper.WriteLine($"异常链 (深度 {snapshot.ExceptionChain.Depth}):");
            foreach (var frame in snapshot.ExceptionChain.Frames)
            {
                var prefix = frame.Depth == 0 ? "→" : "↳";
                TerminalHelper.WriteLine($"  {prefix} [{frame.Depth}] {frame.ExceptionType}: {frame.Message}");
                if (frame.ErrorCode is not null)
                    TerminalHelper.WriteLine($"       错误码: {frame.ErrorCode}");
            }
            TerminalHelper.WriteLine();
        }

        if (snapshot.StackTrace is not null)
        {
            TerminalHelper.WriteLine("堆栈:");
            var lines = snapshot.StackTrace.Split('\n');
            foreach (var line in lines.Take(15))
                TerminalHelper.WriteLine($"  {line.TrimEnd('\r')}");
            if (lines.Length > 15)
                TerminalHelper.WriteLine($"  ... (共 {lines.Length} 行)");
            TerminalHelper.WriteLine();
        }

        var ctx = snapshot.ExecutionContext;
        if (ctx.OperationName is not null || ctx.ToolName is not null || ctx.TurnIndex is not null)
        {
            TerminalHelper.WriteLine("执行上下文:");
            if (ctx.OperationName is not null) TerminalHelper.WriteLine($"  操作: {ctx.OperationName}");
            if (ctx.ToolName is not null) TerminalHelper.WriteLine($"  工具: {ctx.ToolName}");
            if (ctx.ToolGroup is not null) TerminalHelper.WriteLine($"  工具组: {ctx.ToolGroup}");
            if (ctx.TurnIndex is not null) TerminalHelper.WriteLine($"  轮次: {ctx.TurnIndex}");
            if (ctx.RequestId is not null) TerminalHelper.WriteLine($"  请求ID: {ctx.RequestId}");
            if (ctx.SessionId is not null) TerminalHelper.WriteLine($"  会话ID: {ctx.SessionId}");
            if (ctx.ModelId is not null) TerminalHelper.WriteLine($"  模型: {ctx.ModelId}");
            foreach (var (key, value) in ctx.Extra)
                TerminalHelper.WriteLine($"  {key}: {value}");
            TerminalHelper.WriteLine();
        }

        if (snapshot.Tags.Count > 0)
        {
            TerminalHelper.WriteLine("标签:");
            foreach (var (key, value) in snapshot.Tags)
                TerminalHelper.WriteLine($"  {key}: {value}");
            TerminalHelper.WriteLine();
        }

        if (snapshot.Attachments.Count > 0)
        {
            TerminalHelper.WriteLine("附件:");
            foreach (var (name, content) in snapshot.Attachments)
                TerminalHelper.WriteLine($"  {name}: {(content.Length > 200 ? content[..200] + "..." : content)}");
        }
    }
}
