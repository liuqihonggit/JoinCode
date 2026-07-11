
namespace Core.Context.Compact;

/// <summary>
/// 后台记忆提取回调 — 在每轮 LLM 采样后判断是否需要提取持久记忆
/// 对齐 TS extractMemories.ts
/// 核心消费链路：ExtractMemoriesSection.BuildExtractAutoOnlyPrompt/BuildExtractCombinedPrompt() → IForkSubAgentManager.ForkAsync()
/// </summary>
[Register]
public sealed class ExtractMemoriesCallback : IPostSamplingCallback
{
    private const string EditToolName = "Edit";
    private const string ReadToolName = "Read";
    private const string GrepToolName = "Grep";
    private const string GlobToolName = "Glob";

    private readonly IFileSystem _fileSystem;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<ExtractMemoriesCallback>? _logger;

    private int _turnsSinceLastExtraction;
    private bool _inProgress;

    public ExtractMemoriesCallback(
        IFileSystem fileSystem,
        IForkSubAgentManager? forkManager = null,
        ILogger<ExtractMemoriesCallback>? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _forkManager = forkManager;
        _logger = logger;
    }

    public async Task OnPostSamplingAsync(PostSamplingContext context)
    {
        if (context.QuerySource != "repl_main_thread") return;
        if (_inProgress) return;

        _turnsSinceLastExtraction++;

        if (_turnsSinceLastExtraction < 1) return;

        _turnsSinceLastExtraction = 0;
        _inProgress = true;

        try
        {
            var memoryDir = GetMemoryDirectory();
            var existingMemories = FormatMemoryManifest(memoryDir);

            var newMessageCount = context.EstimatedTokenCount > 0 ? Math.Max(1, context.EstimatedTokenCount / 100) : 5;
            var skipIndex = !_fileSystem.FileExists(Path.Combine(memoryDir, "MEMORY.md"));

            var userPrompt = ExtractMemoriesSection.BuildExtractAutoOnlyPrompt(newMessageCount, existingMemories, skipIndex);

            _logger?.LogDebug("ExtractMemories 提取提示词已构建，长度={Length}", userPrompt.Length);

            if (_forkManager is not null && context.SessionId is not null)
            {
                var forkOptions = new ForkOptions
                {
                    ParentSessionId = context.SessionId,
                    TaskDescription = "extract_memories",
                    AllowedTools = [ReadToolName, GrepToolName, GlobToolName, EditToolName],
                    UseExactTools = true,
                    RunInBackground = true,
                    ShareCache = false,
                    ShareContext = false,
                    MaxIterations = 5,
                    SystemPrompt = "你是记忆提取子代理。你的唯一任务是读取和更新记忆文件，然后停止。不要调用任何其他工具。"
                };

                var result = await _forkManager.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);

                _logger?.LogDebug("ExtractMemories forked agent 完成: ForkId={ForkId}, State={State}",
                    result.ForkId, result.State);
            }
            else
            {
                _logger?.LogDebug("ExtractMemories forked agent 不可用（IForkSubAgentManager 或 SessionId 缺失），跳过执行");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ExtractMemories 提取回调执行失败");
        }
        finally
        {
            _inProgress = false;
        }
    }

    private string GetMemoryDirectory()
    {
        var cwd = _fileSystem.GetCurrentDirectory();
        return Path.Combine(cwd, ".jcc", "memory");
    }

    private string FormatMemoryManifest(string memoryDir)
    {
        if (!_fileSystem.DirectoryExists(memoryDir))
            return string.Empty;

        try
        {
            var files = _fileSystem.GetFiles(memoryDir, "*.md", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## 现有记忆文件");
            sb.AppendLine();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                sb.AppendLine($"- {name}.md");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "扫描记忆目录失败: {Dir}", memoryDir);
            return string.Empty;
        }
    }
}
