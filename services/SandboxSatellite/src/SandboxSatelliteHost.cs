namespace JoinCode.SandboxSatellite;

using JoinCode.Abstractions.Security.Sandbox.Ipc;
using Infrastructure.Windows.JobObject;

public sealed class SandboxSatelliteHost : IAsyncDisposable
{
    private readonly IFileSystem _fs;
    private readonly CancellationTokenSource _cts = new();
    private WindowsJobObjectSandbox? _innerJobObject;
    private LinuxCgroupSandbox? _innerCgroup;

    public SandboxSatelliteHost(IFileSystem fs)
    {
        _fs = fs;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            while (!linkedCt.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(linkedCt).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                await ProcessRequestAsync(line, linkedCt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessRequestAsync(string line, CancellationToken ct)
    {
        SandboxIpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(line, SandboxIpcJsonContext.Default.SandboxIpcRequest);
        }
        catch (Exception ex)
        {
            await WriteResponseAsync(new SandboxIpcResponse
            {
                Type = "error",
                RequestId = "",
                Success = false,
                Error = $"Failed to parse request: {ex.Message}"
            }, ct).ConfigureAwait(false);
            return;
        }

        if (request is null)
        {
            return;
        }

        try
        {
            switch (request.Type)
            {
                case "execute":
                    await HandleExecuteAsync(request, ct).ConfigureAwait(false);
                    break;
                case "ping":
                    await WriteResponseAsync(new SandboxIpcResponse
                    {
                        Type = "pong",
                        RequestId = request.RequestId,
                        Success = true
                    }, ct).ConfigureAwait(false);
                    break;
                case "shutdown":
                    _cts.Cancel();
                    await WriteResponseAsync(new SandboxIpcResponse
                    {
                        Type = "shutdown_ack",
                        RequestId = request.RequestId,
                        Success = true
                    }, ct).ConfigureAwait(false);
                    break;
                default:
                    await WriteResponseAsync(new SandboxIpcResponse
                    {
                        Type = "error",
                        RequestId = request.RequestId,
                        Success = false,
                        Error = $"Unknown request type: {request.Type}"
                    }, ct).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            await WriteResponseAsync(new SandboxIpcResponse
            {
                Type = "error",
                RequestId = request.RequestId,
                Success = false,
                Error = ex.Message
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleExecuteAsync(SandboxIpcRequest request, CancellationToken ct)
    {
        var execRequest = JsonSerializer.Deserialize(request.Payload ?? "", SandboxIpcJsonContext.Default.SandboxExecuteRequest);
        if (execRequest is null)
        {
            await WriteResponseAsync(new SandboxIpcResponse
            {
                Type = "execute_result",
                RequestId = request.RequestId,
                Success = false,
                Error = "Failed to parse execute request payload"
            }, ct).ConfigureAwait(false);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? $"/c {execRequest.Command}"
                : $"-c {EscapeForSingleQuotedShell(execRequest.Command)}",
            WorkingDirectory = execRequest.WorkingDirectory ?? _fs.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (execRequest.EnvironmentVariables is not null)
        {
            foreach (var kv in execRequest.EnvironmentVariables)
            {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            await WriteResponseAsync(new SandboxIpcResponse
            {
                Type = "execute_result",
                RequestId = request.RequestId,
                Success = false,
                Error = "Failed to start process"
            }, ct).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            EnsureInnerJobObject();
            if (_innerJobObject is not null && !_innerJobObject.AssignProcess(process.Id))
            {
                Console.Error.WriteLine($"[SandboxSatellite] 将子进程 {process.Id} 加入 JobObject 失败");
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            EnsureInnerCgroup();
            if (_innerCgroup is not null && !_innerCgroup.AssignProcess(process.Id))
            {
                Console.Error.WriteLine($"[SandboxSatellite] 将子进程 {process.Id} 加入 cgroup 失败");
            }
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var completed = process.WaitForExit(execRequest.TimeoutMs);
        if (!completed)
        {
            process.Kill(entireProcessTree: true);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var result = new SandboxExecuteResponse
        {
            StandardOutput = stdout,
            StandardError = stderr,
            ExitCode = process.ExitCode,
            Success = completed && process.ExitCode == 0
        };

        var resultJson = JsonSerializer.Serialize(result, SandboxIpcJsonContext.Default.SandboxExecuteResponse);

        await WriteResponseAsync(new SandboxIpcResponse
        {
            Type = "execute_result",
            RequestId = request.RequestId,
            Success = true,
            Payload = resultJson
        }, ct).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(SandboxIpcResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(response, SandboxIpcJsonContext.Default.SandboxIpcResponse);
        await Console.Out.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await Console.Out.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string EscapeForSingleQuotedShell(string command)
    {
        if (command.Length == 0)
        {
            return "''";
        }

        var escaped = command.Replace("'", @"'\''");
        return $"'{escaped}'";
    }

    private void EnsureInnerJobObject()
    {
        if (_innerJobObject is not null)
        {
            return;
        }

        try
        {
            _innerJobObject = new WindowsJobObjectSandbox();
            _innerJobObject.CreateJobObject();
            Console.Error.WriteLine("[SandboxSatellite] 内部 JobObject 已创建，子进程将受 KILL_ON_JOB_CLOSE 管理");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SandboxSatellite] 创建内部 JobObject 失败: {ex.Message}");
            _innerJobObject = null;
        }
    }

    private void EnsureInnerCgroup()
    {
        if (_innerCgroup is not null)
        {
            return;
        }

        try
        {
            _innerCgroup = new LinuxCgroupSandbox();
            if (!_innerCgroup.CreateCgroup())
            {
                Console.Error.WriteLine("[SandboxSatellite] 创建内部 cgroup 失败，子进程不受 cgroup 隔离");
                _innerCgroup = null;
            }
            else
            {
                Console.Error.WriteLine("[SandboxSatellite] 内部 cgroup 已创建，子进程将受 cgroup 管理");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SandboxSatellite] 创建内部 cgroup 异常: {ex.Message}");
            _innerCgroup = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_innerJobObject is not null)
        {
            try
            {
                _innerJobObject.TerminateAllProcesses();
                _innerJobObject.Dispose();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SandboxSatellite] 销毁内部 JobObject 异常: {ex.Message}");
            }

            _innerJobObject = null;
        }

        if (_innerCgroup is not null)
        {
            try
            {
                await _innerCgroup.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SandboxSatellite] 销毁内部 cgroup 异常: {ex.Message}");
            }

            _innerCgroup = null;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
