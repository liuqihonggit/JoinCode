namespace Core.Context;

/// <summary>
/// 聊天文件上下文服务 — 负责文件路径提取、上下文更新和消息列表转储
/// 提取自 ChatService.UpdateFileContext + DumpMessageList
/// </summary>
[Register]
public sealed partial class ChatFileContextService : IChatFileContextService
{
    [Inject] private readonly FileContextTracker _fileContext;
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<ChatFileContextService>? _logger;

    /// <summary>
    /// 从用户消息中提取文件路径并更新文件上下文
    /// </summary>
    public void UpdateFileContext(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _fileContext.Clear();
            return;
        }

        var paths = FilePathExtractor.ExtractFilePaths(message);
        _fileContext.UpdateFilePaths(paths.ToArray());
        _fileContext.UpdateUserMessage(message);
    }

    /// <summary>
    /// 将每轮对话发送给 API 的完整消息列表转储到 TXT 文件。
    /// 受环境变量 JCC_DUMP_MESSAGES=1 控制，默认不输出。
    /// 文件路径：.x/chat_{sessionId}_turn{N}_iter{M}.txt
    /// turn=用户对话轮次, iter=工具调用迭代次数
    /// </summary>
    public void DumpMessageList(IList<ApiMessage> messages, string sessionId, int conversationTurn, int toolCallIteration)
    {
        if (Environment.GetEnvironmentVariable("JCC_DUMP_MESSAGES") != "1") return;

        try
        {
            var dir = _fs.CombinePath(AppContext.BaseDirectory, ".x");
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);

            var filePath = _fs.CombinePath(dir, $"chat_{sessionId}_turn{conversationTurn}_iter{toolCallIteration}.txt");
            var sb = new System.Text.StringBuilder();

            for (var i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"[{msg.Role}]");
                sb.AppendLine(msg.Content ?? "");

                if (msg.Metadata != null && msg.Metadata.Count > 0)
                {
                    try
                    {
                        var metaJson = JsonSerializer.Serialize(msg.Metadata, ChatServiceJsonContext.Default.DictionaryStringJsonElement);
                        sb.AppendLine($"[Metadata] {metaJson}");
                    }
                    catch (Exception)
                    {
                        sb.AppendLine("[Metadata] <serialization failed>");
                    }
                }
            }

            _fs.WriteAllText(filePath, sb.ToString());
            _logger?.LogInformation("对话消息列表已转储: {FilePath} ({Count} 条消息)", filePath, messages.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "转储对话消息列表失败");
        }
    }
}
