namespace Core.Security.Auditing;

/// <summary>
/// 命令执行审计日志的 JSON 序列化上下文 — AOT 安全
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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
/// </summary>
[Register(typeof(ICommandExecutionAuditor), ServiceLifetime.Singleton)]
public sealed class CommandExecutionAuditor : ICommandExecutionAuditor
{
    private readonly IFileSystem _fs;
    private readonly string _auditDirectory;
    private readonly ILogger<CommandExecutionAuditor>? _logger;
    private readonly Lock _writeLock = new();

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
    {
        _fs = fs;
        _auditDirectory = auditDirectory ?? ".audit";
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Record(CommandExecutionAuditEntry entry)
    {
        try
        {
            _fs.CreateDirectory(_auditDirectory);
            var filePath = Path.Combine(_auditDirectory, $"command-execution-{entry.Timestamp:yyyy-MM-dd}.jsonl");
            var json = RelaxedJsonSerializer.Serialize(entry, CommandAuditJsonContext.Default);

            using (_writeLock.EnterScope())
            {
                _fs.AppendAllText(filePath, json + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "审计日志写入失败: {Command}", entry.Command);
        }
    }
}
