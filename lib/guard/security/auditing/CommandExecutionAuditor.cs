namespace Core.Security.Auditing;

/// <summary>
/// 命令执行审计日志的 JSON 序列化上下文 — AOT 安全
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<CommandDangerLevel>),
        typeof(JsonStringEnumConverter<PermissionMode>),
        typeof(JsonStringEnumConverter<FileChangeType>)
    ])]
[JsonSerializable(typeof(CommandExecutionAuditEntry))]
[JsonSerializable(typeof(FileChangeRecord))]
[JsonSerializable(typeof(List<FileChangeRecord>))]
internal sealed partial class CommandAuditJsonContext : JsonSerializerContext;

/// <summary>
/// 命令执行审计日志实现 — 将审计条目以 JSONL 格式写入 .audit/ 目录
/// <para>
/// 文件路径: .audit/command-execution-{date}.jsonl
/// 每行一个 JSON 对象,使用 RelaxedJsonSerializer 序列化(AOT 安全)
/// 通过 IFileSystem 抽象层写入文件(测试可注入 InMemoryFileSystem)
/// </para>
/// <para>
/// 无锁设计: ActorBase 邮箱模型 — Record 用 Ask 模式(发送命令+等待消费者处理),消费者线程独占文件写入
/// 统一 ActorBase 基础设施: 有界2048 Channel + 背压 + 死锁检测 + 异常容错
/// </para>
/// </summary>
[Register(typeof(ICommandExecutionAuditor), ServiceLifetime.Singleton)]
public sealed class CommandExecutionAuditor : ActorBase<CommandAuditCommand, Unit>, ICommandExecutionAuditor {
    private readonly IFileSystem _fs;
    private readonly string _auditDirectory;
    private readonly ILogger<CommandExecutionAuditor>? _logger;

    /// <summary>
    /// 创建 CommandExecutionAuditor;审计目录默认为 .audit/
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="auditDirectory">审计日志目录路径(可选,默认 .audit/)</param>
    /// <param name="logger">日志器(可选)</param>
    public CommandExecutionAuditor(
        IFileSystem fs,
        string? auditDirectory = null,
        ILogger<CommandExecutionAuditor>? logger = null)
        : base(new ActorBackpressure(2048, BoundedChannelFullMode.DropOldest)) {
        _fs = fs;
        _auditDirectory = auditDirectory ?? ".audit";
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask Record(CommandExecutionAuditEntry entry) {
        var tcs = new TaskCompletionSource();
        await SendAsync(new CommandAuditCommand.RecordEntry(entry, tcs)).ConfigureAwait(false);
        await AskAwait(tcs).ConfigureAwait(false);
    }

    /// <summary>
    /// 命令分发 — 由 Consumer 线程串行调用,文件写入无需锁
    /// </summary>
    protected override ValueTask HandleAsync(CommandAuditCommand cmd, CancellationToken ct) {
        return cmd switch {
            CommandAuditCommand.RecordEntry c => HandleRecordAsync(c),
            _ => ValueTask.CompletedTask
        };
    }

    private async ValueTask HandleRecordAsync(CommandAuditCommand.RecordEntry cmd) {
        try {
            _fs.CreateDirectory(_auditDirectory);
            var filePath = Path.Combine(_auditDirectory, $"command-execution-{cmd.Entry.Timestamp:yyyy-MM-dd}.jsonl");
            var json = RelaxedJsonSerializer.Serialize(cmd.Entry, CommandAuditJsonContext.Default);
            await _fs.AppendAllText(filePath, json + Environment.NewLine).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "审计日志写入失败: {Command}", cmd.Entry.Command);
        } finally {
            cmd.Reply.SetResult();
        }
    }

    /// <summary>
    /// Consumer 异常回调 — 记录日志,不终止 Consumer 循环
    /// </summary>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogWarning(ex, "审计日志 Consumer 异常");
    }
}

/// <summary>
/// CommandExecutionAuditor Actor 命令类型
/// </summary>
public abstract record CommandAuditCommand {
    /// <summary>记录审计条目 — Ask 模式,通过 Reply 返回完成信号</summary>
    public sealed record RecordEntry(CommandExecutionAuditEntry Entry, TaskCompletionSource Reply) : CommandAuditCommand;
}
