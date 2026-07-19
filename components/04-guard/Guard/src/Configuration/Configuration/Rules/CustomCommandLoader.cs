namespace Core.Configuration;

public sealed partial class CustomCommandLoader
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<CustomCommandLoader>? _logger;

    private static readonly string[] ProjectCommandDirs = [
        Path.Combine(".trae", "commands"),
        Path.Combine(".claude", "commands"),
        Path.Combine(".codex", "commands")
    ];

    public CustomCommandLoader(IFileSystem fs, ILogger<CustomCommandLoader>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    public async Task<List<CustomCommand>> LoadProjectCommandsAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var commands = new List<CustomCommand>();
        var currentDirPath = _fs.GetFullPath(workingDirectory);

        while (currentDirPath != null)
        {
            // 并行扫描所有项目命令目录
            var dirTasks = new List<Task<List<CustomCommand>>>();
            foreach (var commandDir in ProjectCommandDirs)
            {
                var fullPath = Path.Combine(currentDirPath, commandDir);
                if (_fs.DirectoryExists(fullPath))
                {
                    dirTasks.Add(LoadCommandsFromDirectoryAsync(fullPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var dirCommands in dirResults)
            {
                commands.AddRange(dirCommands);
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        return Deduplicate(commands);
    }

    public async Task<List<CustomCommand>> LoadUserCommandsAsync(CancellationToken cancellationToken = default)
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 并行扫描所有用户命令目录
        var dirTasks = new List<Task<List<CustomCommand>>>();
        var appDataPath = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder, AppDataConstants.CommandsFolderName);
        if (_fs.DirectoryExists(appDataPath))
        {
            dirTasks.Add(LoadCommandsFromDirectoryAsync(appDataPath, cancellationToken));
        }

        var codexCommandsPath = Path.Combine(userProfile, ".codex", "commands");
        if (_fs.DirectoryExists(codexCommandsPath))
        {
            dirTasks.Add(LoadCommandsFromDirectoryAsync(codexCommandsPath, cancellationToken));
        }

        var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
        var commands = new List<CustomCommand>();
        foreach (var dirCommands in dirResults)
        {
            commands.AddRange(dirCommands);
        }

        return Deduplicate(commands);
    }

    private async Task<List<CustomCommand>> LoadCommandsFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var commands = new List<CustomCommand>();

        try
        {
            var mdFiles = _fs.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories);
            var baseDirLength = directoryPath.Length + 1;

            // 并行读取所有 md 文件
            var readTasks = new List<Task<CustomCommand?>>();
            foreach (var filePath in mdFiles)
            {
                readTasks.Add(TryReadCommandFileAsync(filePath, baseDirLength, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var command in readResults)
            {
                if (command is not null)
                {
                    commands.Add(command);
                    _logger?.LogInformation("已加载自定义命令: {Name} ({Path})", command.FullName, command.SourcePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "扫描自定义命令目录失败: {Path}", directoryPath);
        }

        return commands;
    }

    private async Task<CustomCommand?> TryReadCommandFileAsync(string filePath, int baseDirLength, CancellationToken cancellationToken)
    {
        try
        {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var relativePath = filePath.Length > baseDirLength
                ? filePath[baseDirLength..]
                : Path.GetFileName(filePath);

            return ParseCommandFile(relativePath, content, filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取自定义命令文件失败: {Path}", filePath);
            return null;
        }
    }

    internal static CustomCommand? ParseCommandFile(string relativePath, string content, string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var dirPart = Path.GetDirectoryName(relativePath);
        var ns = !string.IsNullOrEmpty(dirPart)
            ? dirPart.Replace(Path.DirectorySeparatorChar, ':').Replace(Path.AltDirectorySeparatorChar, ':')
            : null;

        var (parsedContent, description, disableModelInvocation) = ParseFrontmatter(content);

        return new CustomCommand
        {
            Name = fileName,
            Content = parsedContent.Trim(),
            Description = description,
            SourcePath = sourcePath,
            DisableModelInvocation = disableModelInvocation,
            Namespace = ns
        };
    }

    internal static (string Content, string Description, bool DisableModelInvocation) ParseFrontmatter(string content)
    {
        var description = string.Empty;
        var disableModelInvocation = false;

        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return (content, description, disableModelInvocation);
        }

        var endIdx = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return (content, description, disableModelInvocation);
        }

        var frontmatter = content[3..endIdx].Trim();
        var body = content[(endIdx + 3)..].TrimStart('\n', '\r');

        foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value.Trim('"', '\'');
            }
            else if (key.Equals("disable-model-invocation", StringComparison.OrdinalIgnoreCase))
            {
                disableModelInvocation = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return (body, description, disableModelInvocation);
    }

    private static List<CustomCommand> Deduplicate(List<CustomCommand> commands)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CustomCommand>();

        foreach (var cmd in commands)
        {
            if (seen.Add(cmd.FullName))
            {
                result.Add(cmd);
            }
        }

        return result;
    }
}
