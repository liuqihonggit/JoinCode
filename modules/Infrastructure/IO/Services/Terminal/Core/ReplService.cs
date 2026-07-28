using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class ReplService : IReplService
{
    private volatile bool _replModeEnabled;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    [Inject] private readonly ILogger<ReplService>? _logger;
    [Inject] private readonly IClockService _clock;

    private static readonly string[] s_hiddenTools =
    [
        FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.Bash,
        "notebook_edit", "agent"
    ];

    private static readonly (string Language, string DisplayName, string Executable, string InstallHint)[] s_languageDefinitions =
    [
        ("csharp", "C#", "dotnet-script", "dotnet tool install -g dotnet-script"),
        ("powershell", "PowerShell", "pwsh", "https://github.com/PowerShell/PowerShell"),
        ("python", "Python", "python3", "https://www.python.org/downloads/"),
    ];

    private readonly Lazy<IReadOnlyList<ReplLanguageInfo>> _availableLanguages;

    public ReplService(IFileSystem fs, IProcessService processService, ILogger<ReplService>? logger = null, IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _replModeEnabled = IsEnvTruthy(JccEnvVar.ReplMode.ToValue());
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _availableLanguages = new Lazy<IReadOnlyList<ReplLanguageInfo>>(DetectAvailableLanguages, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsReplModeEnabled => _replModeEnabled;

    public void EnableReplMode()
    {
        _replModeEnabled = true;
        _logger?.LogInformation("REPL 模式已启用");
    }

    public void DisableReplMode()
    {
        _replModeEnabled = false;
        _logger?.LogInformation("REPL 模式已禁用");
    }

    public async Task<ReplResult> ExecuteAsync(string code, string language = "csharp", int timeoutSeconds = 30, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = language,
                ExecutionTime = TimeSpan.Zero,
                Error = "执行被取消"
            };
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = language,
                ExecutionTime = TimeSpan.Zero,
                Error = "代码不能为空"
            };
        }

        var startTime = _clock.GetUtcNow();

        try
        {
            var result = language.ToLowerInvariant() switch
            {
                "csharp" or "c#" => await ExecuteCSharpAsync(code, timeoutSeconds, ct).ConfigureAwait(false),
                "powershell" or "ps1" => await ExecutePowerShellAsync(code, timeoutSeconds, ct).ConfigureAwait(false),
                "python" or "py" => await ExecutePythonAsync(code, timeoutSeconds, ct).ConfigureAwait(false),
                _ => new ReplResult
                {
                    Success = false,
                    Output = string.Empty,
                    Language = language,
                    ExecutionTime = _clock.GetUtcNow() - startTime,
                    Error = $"不支持的语言: {language}。支持: csharp, powershell, python"
                }
            };

            return result;
        }
        catch (OperationCanceledException)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = language,
                ExecutionTime = _clock.GetUtcNow() - startTime,
                Error = "执行被取消"
            };
        }
        catch (Exception ex)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = language,
                ExecutionTime = _clock.GetUtcNow() - startTime,
                Error = $"执行失败: {ex.Message}"
            };
        }
    }

    public IReadOnlyList<string> GetHiddenTools() => _replModeEnabled ? s_hiddenTools : Array.Empty<string>();

    public IReadOnlyList<ReplLanguageInfo> GetAvailableLanguages() => _availableLanguages.Value;

    private IReadOnlyList<ReplLanguageInfo> DetectAvailableLanguages()
    {
        var result = new List<ReplLanguageInfo>(s_languageDefinitions.Length);

        foreach (var def in s_languageDefinitions)
        {
            var executable = ResolveExecutable(def.Executable, def.Language);
            var isAvailable = executable != null;

            result.Add(new ReplLanguageInfo
            {
                Language = def.Language,
                DisplayName = def.DisplayName,
                Executable = executable ?? def.Executable,
                IsAvailable = isAvailable,
                InstallHint = isAvailable ? null : def.InstallHint
            });
        }

        return result.AsReadOnly();
    }

    private string? ResolveExecutable(string primaryName, string language)
    {
        var candidates = GetCandidateExecutables(primaryName, language);

        foreach (var candidate in candidates)
        {
            var fullPath = _processService.FindExecutableAsync(candidate).GetAwaiter().GetResult();
            if (fullPath != null)
            {
                return fullPath;
            }
        }

        return null;
    }

    private static string[] GetCandidateExecutables(string primaryName, string language)
    {
        if (OperatingSystem.IsWindows())
        {
            return language switch
            {
                "powershell" => ["pwsh", "powershell"],
                "python" => ["python", "python3", "py"],
                _ => [primaryName]
            };
        }

        return language switch
        {
            "powershell" => ["pwsh"],
            "python" => ["python3", "python"],
            _ => [primaryName]
        };
    }

    private async Task<ReplResult> ExecuteCSharpAsync(string code, int timeoutSeconds, CancellationToken ct)
    {
        var executable = ResolveExecutable("dotnet-script", "csharp");
        if (executable == null)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = "csharp",
                ExecutionTime = TimeSpan.Zero,
                Error = "dotnet-script 未安装。请执行: dotnet tool install -g dotnet-script"
            };
        }

        var scriptFile = _fs.CombinePath(Path.GetTempPath(), $"jcc_repl_{Guid.NewGuid():N}.csx");
        var output = new System.Text.StringBuilder();

        try
        {
            await _fs.WriteAllTextAsync(scriptFile, code, ct).ConfigureAwait(false);

            var result = await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = executable,
                Arguments = $"\"{scriptFile}\"",
                TimeoutMs = timeoutSeconds * 1000
            }, ct).ConfigureAwait(false);

            output.AppendLine(result.StandardOutput);

            return new ReplResult
            {
                Success = result.Success,
                Output = output.ToString(),
                Language = "csharp",
                ExecutionTime = result.ExecutionTime,
                Error = result.Success ? null : result.StandardError
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ReplResult
            {
                Success = false,
                Output = output.ToString(),
                Language = "csharp",
                ExecutionTime = TimeSpan.Zero,
                Error = $"dotnet-script 执行失败: {ex.Message}"
            };
        }
        finally
        {
            try { if (_fs.FileExists(scriptFile)) _fs.DeleteFile(scriptFile); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ReplService: failed to delete C# script file: {ex.Message}"); }
        }
    }

    private async Task<ReplResult> ExecutePowerShellAsync(string code, int timeoutSeconds, CancellationToken ct)
    {
        var executable = ResolveExecutable("pwsh", "powershell");
        if (executable == null)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = "powershell",
                ExecutionTime = TimeSpan.Zero,
                Error = "PowerShell 未安装。请访问: https://github.com/PowerShell/PowerShell"
            };
        }

        var scriptFile = _fs.CombinePath(Path.GetTempPath(), $"jcc_repl_{Guid.NewGuid():N}.ps1");

        try
        {
            await _fs.WriteAllTextAsync(scriptFile, code, ct).ConfigureAwait(false);

            var result = await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = executable,
                Arguments = "-NoProfile -NonInteractive -File \"" + scriptFile + "\"",
                TimeoutMs = timeoutSeconds * 1000
            }, ct).ConfigureAwait(false);

            return new ReplResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Language = "powershell",
                ExecutionTime = result.ExecutionTime,
                Error = result.Success ? null : result.StandardError
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = "powershell",
                ExecutionTime = TimeSpan.Zero,
                Error = $"PowerShell 执行失败: {ex.Message}"
            };
        }
        finally
        {
            try { if (_fs.FileExists(scriptFile)) _fs.DeleteFile(scriptFile); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ReplService: failed to delete PowerShell script file: {ex.Message}"); }
        }
    }

    private async Task<ReplResult> ExecutePythonAsync(string code, int timeoutSeconds, CancellationToken ct)
    {
        var executable = ResolveExecutable("python3", "python");
        if (executable == null)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = "python",
                ExecutionTime = TimeSpan.Zero,
                Error = "Python 未安装。请访问: https://www.python.org/downloads/"
            };
        }

        var scriptFile = _fs.CombinePath(Path.GetTempPath(), $"jcc_repl_{Guid.NewGuid():N}.py");

        try
        {
            await _fs.WriteAllTextAsync(scriptFile, code, ct).ConfigureAwait(false);

            var result = await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = executable,
                Arguments = $"\"{scriptFile}\"",
                TimeoutMs = timeoutSeconds * 1000
            }, ct).ConfigureAwait(false);

            return new ReplResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Language = "python",
                ExecutionTime = result.ExecutionTime,
                Error = result.Success ? null : result.StandardError
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ReplResult
            {
                Success = false,
                Output = string.Empty,
                Language = "python",
                ExecutionTime = TimeSpan.Zero,
                Error = $"Python 执行失败: {ex.Message}"
            };
        }
        finally
        {
            try { if (_fs.FileExists(scriptFile)) _fs.DeleteFile(scriptFile); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ReplService: failed to delete Python script file: {ex.Message}"); }
        }
    }

    private static bool IsEnvTruthy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(value) && value is not "0" and not "false";
    }
}
