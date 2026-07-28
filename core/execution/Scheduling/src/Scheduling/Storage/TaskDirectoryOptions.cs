
namespace Core.Scheduling;

/// <summary>
/// 任务目录配置选项
/// </summary>
public sealed class TaskDirectoryOptions
{
    /// <summary>
    /// 任务目录路径，默认为 .jcc/tasks
    /// </summary>
    public string TaskDirectoryPath { get; set; } = Path.Combine(
        WorkflowConstants.Paths.JccDirectory,
        "tasks");

    /// <summary>
    /// 高水位标记文件名
    /// </summary>
    public const string HighWaterMarkFileName = ".highwatermark";

    /// <summary>
    /// 任务文件扩展名
    /// </summary>
    public const string TaskFileExtension = ".json";

    /// <summary>
    /// 任务文件前缀
    /// </summary>
    public const string TaskFilePrefix = "task-";

    /// <summary>
    /// 获取高水位标记文件完整路径
    /// </summary>
    public string GetHighWaterMarkPath()
    {
        return Path.Combine(TaskDirectoryPath, HighWaterMarkFileName);
    }

    /// <summary>
    /// 获取任务文件完整路径
    /// </summary>
    public string GetTaskFilePath(string taskId)
    {
        return Path.Combine(TaskDirectoryPath, $"{TaskFilePrefix}{taskId}{TaskFileExtension}");
    }

    /// <summary>
    /// 从文件名解析任务ID
    /// </summary>
    public static string? ParseTaskIdFromFileName(string fileName)
    {
        if (!fileName.StartsWith(TaskFilePrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!fileName.EndsWith(TaskFileExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        var startIndex = TaskFilePrefix.Length;
        var length = fileName.Length - TaskFilePrefix.Length - TaskFileExtension.Length;

        if (length <= 0)
            return null;

        return fileName.Substring(startIndex, length);
    }
}

/// <summary>
/// 任务目录配置构建器 - 支持链式配置
/// </summary>
public sealed class TaskDirectoryOptionsBuilder
{
    private string _taskDirectoryPath = Path.Combine(
        WorkflowConstants.Paths.JccDirectory,
        "tasks");

    private TaskDirectoryOptionsBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static TaskDirectoryOptionsBuilder Create() => new();

    /// <summary>
    /// 设置任务目录路径
    /// </summary>
    public TaskDirectoryOptionsBuilder WithTaskDirectoryPath(string path)
    {
        _taskDirectoryPath = path;
        return this;
    }

    /// <summary>
    /// 使用用户配置文件目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseUserProfileDirectory()
    {
        _taskDirectoryPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            "tasks");
        return this;
    }

    /// <summary>
    /// 使用本地应用数据目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseLocalAppDataDirectory()
    {
        _taskDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataConstants.AppDataFolder,
            "tasks");
        return this;
    }

    /// <summary>
    /// 使用应用数据目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseAppDataDirectory()
    {
        _taskDirectoryPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            "tasks");
        return this;
    }

    /// <summary>
    /// 使用临时目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseTempDirectory()
    {
        _taskDirectoryPath = Path.Combine(Path.GetTempPath(), AppDataConstants.AppDataFolder, "tasks");
        return this;
    }

    /// <summary>
    /// 使用当前工作目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseCurrentDirectory(IFileSystem fs, string? subPath = null)
    {
        _taskDirectoryPath = Path.Combine(fs.GetCurrentDirectory(), subPath ?? $"{AppDataConstants.AppDataFolder}/tasks");
        return this;
    }

    /// <summary>
    /// 使用自定义基础目录
    /// </summary>
    public TaskDirectoryOptionsBuilder UseCustomBaseDirectory(string baseDirectory, string subPath = "tasks")
    {
        _taskDirectoryPath = Path.Combine(baseDirectory, subPath);
        return this;
    }

    /// <summary>
    /// 构建任务目录配置
    /// </summary>
    public TaskDirectoryOptions Build()
    {
        return new TaskDirectoryOptions
        {
            TaskDirectoryPath = _taskDirectoryPath
        };
    }
}
