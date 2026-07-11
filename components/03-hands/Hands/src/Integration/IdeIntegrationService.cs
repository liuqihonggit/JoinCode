using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class IdeIntegrationService : IIdeIntegrationService
{
    private IdeInfo? _currentConnection;
    private string? _currentFilePath;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    [Inject] private readonly ILogger<IdeIntegrationService>? _logger;

    private static readonly FrozenDictionary<IdeType, IdeDetectionConfig> DetectionConfigs =
        new Dictionary<IdeType, IdeDetectionConfig>
        {
            [IdeType.VsCode] = new("code", "Visual Studio Code",
                WindowsPaths: ["Programs\\Microsoft VS Code\\Code.exe"],
                ProcessNames: ["Code.exe"]),
            [IdeType.Cursor] = new("cursor", "Cursor",
                WindowsPaths: ["Programs\\Cursor\\Cursor.exe"],
                ProcessNames: ["Cursor.exe"]),
            [IdeType.Windsurf] = new("windsurf", "Windsurf",
                WindowsPaths: ["Programs\\Windsurf\\Windsurf.exe"],
                ProcessNames: ["Windsurf.exe"]),
            [IdeType.JetBrains] = new(null, "JetBrains IDE",
                WindowsPaths:
                [
                    "Programs\\IntelliJ IDEA\\bin\\idea64.exe",
                    "Programs\\JetBrains\\Toolbox\\apps\\IDEA-U\\ch-0",
                    "Programs\\PyCharm\\bin\\pycharm64.exe",
                    "Programs\\Rider\\bin\\rider64.exe",
                    "Programs\\WebStorm\\bin\\webstorm64.exe",
                    "Programs\\GoLand\\bin\\goland64.exe",
                    "Programs\\CLion\\bin\\clion64.exe"
                ],
                ProcessNames: ["idea64.exe", "pycharm64.exe", "rider64.exe", "webstorm64.exe", "goland64.exe", "clion64.exe"])
        }.ToFrozenDictionary();

    public IdeIntegrationService(IFileSystem fs, IProcessService processService, ILogger<IdeIntegrationService>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    public IdeInfo? CurrentConnection => _currentConnection;
    public string? CurrentFilePath => _currentFilePath;

    public IReadOnlyList<IdeInfo> DetectInstalledIdes()
    {
        var ides = new List<IdeInfo>();

        foreach (var (ideType, config) in DetectionConfigs)
        {
            var detected = DetectIde(ideType, config);
            if (detected != null)
                ides.Add(detected);
        }

        return ides.AsReadOnly();
    }

    public IReadOnlyList<IdeDetectionDetail> DetectInstalledIdesDetailed()
    {
        var results = new List<IdeDetectionDetail>();

        foreach (var (ideType, config) in DetectionConfigs)
        {
            var pathResult = ScanInstallPaths(ideType, config, _fs);
            var pathResult2 = CheckCommandPath(config.Command);
            var processRunning = CheckProcessRunning(config.ProcessNames);

            results.Add(new IdeDetectionDetail
            {
                Type = ideType,
                Name = config.DisplayName,
                FoundOnPath = pathResult2 != null,
                Path = pathResult ?? pathResult2,
                IsRunning = processRunning,
                ExtensionInstalled = pathResult != null || pathResult2 != null
            });
        }

        return results.AsReadOnly();
    }

    public Task<bool> ConnectAsync(IdeType ideType, CancellationToken ct = default)
    {
        var ides = DetectInstalledIdes();
        var ide = ides.FirstOrDefault(i => i.Type == ideType);

        if (ide == null)
        {
            _logger?.LogWarning("IDE {Type} 未检测到", ideType);
            return Task.FromResult(false);
        }

        _currentConnection = ide with { IsConnected = true };
        _logger?.LogInformation("已连接到 IDE: {Name}", ide.Name);
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_currentConnection != null)
        {
            _logger?.LogInformation("已断开 IDE 连接: {Name}", _currentConnection.Name);
            _currentConnection = null;
            _currentFilePath = null;
        }
        return Task.CompletedTask;
    }

    public async Task<bool> OpenFileAsync(string filePath, int? line = null, CancellationToken ct = default)
    {
        if (_currentConnection == null)
        {
            _logger?.LogWarning("无法打开文件：未连接 IDE");
            return false;
        }

        try
        {
            var command = GetIdeCommand(_currentConnection.Type);
            if (command == null)
            {
                _logger?.LogWarning("不支持的 IDE 类型: {Type}", _currentConnection.Type);
                return false;
            }

            var args = $"--goto \"{filePath}\"";
            if (line.HasValue)
                args += $":{line.Value}";

            await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = command,
                Arguments = args,
                TimeoutMs = 5000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, ct).ConfigureAwait(false);

            _currentFilePath = filePath;
            _logger?.LogInformation("已在 IDE 中打开文件: {FilePath} (行: {Line})", filePath, line);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开文件失败: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// 在 IDE 中设置选区 — 对齐 TS bridgeMessaging.ts setSelection
    /// 实现：通过 IDE CLI --goto 定位光标到起始行（列信息部分 IDE 不支持，仅传行号）
    /// 限制：CLI 方式只能定位光标，无法精确选中范围（endLine/endCol 当前忽略）
    /// </summary>
    public async Task<bool> SetSelectionAsync(string filePath, int startLine, int startCol, int endLine, int endCol, CancellationToken ct = default)
    {
        if (_currentConnection == null)
        {
            _logger?.LogWarning("无法设置选区：未连接 IDE");
            return false;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger?.LogWarning("无法设置选区：文件路径为空");
            return false;
        }

        if (startLine < 1)
        {
            _logger?.LogWarning("无法设置选区：起始行号无效 {StartLine}", startLine);
            return false;
        }

        try
        {
            var command = GetIdeCommand(_currentConnection.Type);
            if (command == null)
            {
                _logger?.LogWarning("不支持的 IDE 类型: {Type}", _currentConnection.Type);
                return false;
            }

            // VSCode/Cursor/Windsurf 支持 --goto file:line:col
            // JetBrains 仅支持 --line N（不支持列）
            var args = _currentConnection.Type == IdeType.JetBrains
                ? $"--line {startLine} \"{filePath}\""
                : $"--goto \"{filePath}:{startLine}:{startCol}\"";

            await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = command,
                Arguments = args,
                TimeoutMs = 5000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, ct).ConfigureAwait(false);

            _currentFilePath = filePath;
            _logger?.LogInformation("已在 IDE 中设置选区: {FilePath} (行: {Line}, 列: {Col})", filePath, startLine, startCol);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置选区失败: {FilePath}", filePath);
            return false;
        }
    }

    private static string? GetIdeCommand(IdeType type)
    {
        return type switch
        {
            IdeType.VsCode => "code",
            IdeType.Cursor => "cursor",
            IdeType.Windsurf => "windsurf",
            IdeType.JetBrains => "idea64",
            _ => null
        };
    }

    private IdeInfo? DetectIde(IdeType type, IdeDetectionConfig config)
    {
        var pathResult = ScanInstallPaths(type, config, _fs);
        var pathResult2 = CheckCommandPath(config.Command);

        if (pathResult == null && pathResult2 == null)
            return null;

        return new IdeInfo
        {
            Type = type,
            Name = config.DisplayName,
            ExtensionInstalled = true,
            IsConnected = false
        };
    }

    private static string? ScanInstallPaths(IdeType type, IdeDetectionConfig config, IFileSystem fs)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

        foreach (var relativePath in config.WindowsPaths)
        {
            var candidates = new List<string?>();
            if (localAppData != null)
                candidates.Add(fs.CombinePath(localAppData, relativePath));
            if (programFiles != null)
                candidates.Add(fs.CombinePath(programFiles, relativePath));
            if (programFilesX86 != null)
                candidates.Add(fs.CombinePath(programFilesX86, relativePath));

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;

                if (type == IdeType.JetBrains && fs.DirectoryExists(candidate))
                    return candidate;

                if (fs.FileExists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private string? CheckCommandPath(string? command)
    {
        if (string.IsNullOrEmpty(command))
            return null;

        try
        {
            return _processService.FindExecutableAsync(command).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private bool CheckProcessRunning(string[] processNames)
    {
        try
        {
            foreach (var name in processNames)
            {
                var processName = System.IO.Path.GetFileNameWithoutExtension(name);
                if (_processService.IsProcessRunning(processName))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private sealed record IdeDetectionConfig(
        string? Command,
        string DisplayName,
        string[] WindowsPaths,
        string[] ProcessNames);
}