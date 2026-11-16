// LSP JSON-RPC 连接 —— exe 自持服务器进程，Content-Length 分帧，纯 BCL。
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JoinCode.CliNode.Code;

/// <summary>
/// 一次 LSP 会话：起服务器 → initialize → 按需 didOpen/请求 → shutdown。
/// <para>短生命周期是刻意选择：exe 每次调用起一个服务器，退出即关，不在跨调用间保留宿主式常驻实例，
/// 因此不存在"宿主索引态"这第二份真相源；代价是首次请求要等服务器索引（由 request timeout 兜底）。</para>
/// </summary>
internal sealed class LspConnection : IAsyncDisposable {
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(
        int.TryParse(Environment.GetEnvironmentVariable("JCC_LSP_REQUEST_TIMEOUT_SECONDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : 180);

    private readonly Process _process;
    private readonly Stream _toServer;
    private readonly Stream _fromServer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _readCts = new();
    private readonly StringBuilder _stderrTail = new();
    private int _nextId;
    private int _disposed;

    private LspConnection(Process process) {
        _process = process;
        _toServer = process.StandardInput.BaseStream;
        _fromServer = process.StandardOutput.BaseStream;
    }

    public static async Task<LspConnection> StartAsync(LspServerEntry entry, string rootDir, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(entry.Command))
            throw new InvalidOperationException($"LSP 服务器 '{entry.ServerId}' 没配 command，无法启动。");

        var startInfo = new ProcessStartInfo(entry.Command) {
            WorkingDirectory = entry.WorkingDirectory ?? rootDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in entry.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in entry.Environment) startInfo.Environment[key] = value;

        Process? process;
        try {
            process = Process.Start(startInfo);
        } catch (Exception ex) {
            throw new InvalidOperationException($"启动 LSP 服务器失败: {entry.Command}（{ex.Message}）。确认它已安装且在 PATH 上。", ex);
        }
        if (process is null) throw new InvalidOperationException($"启动 LSP 服务器失败: {entry.Command}");

        var connection = new LspConnection(process);
        connection.StartPumps();
        try {
            await connection.InitializeAsync(entry, rootDir, ct).ConfigureAwait(false);
        } catch {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return connection;
    }

    private void StartPumps() {
        _ = Task.Run(async () => {
            try {
                await ReadLoopAsync(_readCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // 关闭中
            } catch (Exception) {
                FailAllPending();
            }
        }, CancellationToken.None);

        // stderr 必须排空，否则服务器写满管道会阻塞
        _ = Task.Run(async () => {
            try {
                var buffer = new char[4096];
                int read;
                while ((read = await _process.StandardError.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0) {
                    lock (_stderrTail) {
                        _stderrTail.Append(buffer, 0, read);
                        if (_stderrTail.Length > 4000) _stderrTail.Remove(0, _stderrTail.Length - 4000);
                    }
                }
            } catch (Exception) {
                // 进程已退出
            }
        }, CancellationToken.None);
    }

    private async Task InitializeAsync(LspServerEntry entry, string rootDir, CancellationToken ct) {
        var parameters = new JsonObject {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = new Uri(rootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri,
            ["capabilities"] = new JsonObject()
        };
        var timeout = TimeSpan.FromSeconds(entry.StartupTimeoutSeconds > 0 ? entry.StartupTimeoutSeconds : 30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var result = await RequestAsync("initialize", parameters, cts.Token).ConfigureAwait(false);
        if (result is null) throw new InvalidOperationException($"LSP 服务器 '{entry.ServerId}' initialize 无响应" + StderrHint());
        await NotifyAsync("initialized", null, ct).ConfigureAwait(false);
    }

    private string StderrHint() {
        lock (_stderrTail) {
            if (_stderrTail.Length == 0) return "。";
            var tail = _stderrTail.ToString().Trim();
            return "。服务器 stderr 末尾：" + (tail.Length > 600 ? tail[^600..] : tail);
        }
    }

    public async Task OpenFileAsync(string filePath, string languageId, CancellationToken ct) {
        if (!_openedFiles.Add(filePath)) return;
        var text = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var parameters = new JsonObject {
            ["textDocument"] = new JsonObject {
                ["uri"] = new Uri(filePath).AbsoluteUri,
                ["languageId"] = languageId,
                ["version"] = 1,
                ["text"] = text
            }
        };
        await NotifyAsync("textDocument/didOpen", parameters, ct).ConfigureAwait(false);
    }

    public async Task NotifyAsync(string method, JsonNode? parameters, CancellationToken ct) {
        var message = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };
        if (parameters is not null) message["params"] = parameters;
        await WriteAsync(message, ct).ConfigureAwait(false);
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters, CancellationToken ct) {
        var id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        var message = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null) message["params"] = parameters;

        var pending = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try {
            await WriteAsync(message, ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DefaultRequestTimeout);
            try {
                return await pending.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                throw new TimeoutException($"LSP 请求 {method} 超时（{DefaultRequestTimeout.TotalSeconds:F0}s）" + StderrHint());
            }
        } finally {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task WriteAsync(JsonNode message, CancellationToken ct) {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            await _toServer.WriteAsync(header, ct).ConfigureAwait(false);
            await _toServer.WriteAsync(body, ct).ConfigureAwait(false);
            await _toServer.FlushAsync(ct).ConfigureAwait(false);
        } finally {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            var body = await ReadMessageAsync(_fromServer, ct).ConfigureAwait(false);
            if (body is null) {
                FailAllPending();
                return;
            }
            JsonNode? message;
            try {
                message = JsonNode.Parse(body);
            } catch (JsonException) {
                continue;
            }
            var id = LspJson.Str(message?["id"]);
            var method = LspJson.Str(message?["method"]);
            if (id.Length > 0 && _pending.TryRemove(id, out var pending)) {
                pending.TrySetResult(message?["result"]);
                continue;
            }
            if (method.Length > 0 && id.Length == 0) {
                // 服务器通知（发布诊断、索引进度等）：短生命周期 exe 不消费，直接丢弃
                continue;
            }
            if (method.Length > 0) {
                // 服务器反向请求（client/registerCapability、window/workDoneProgress/create …）：回 null 表示不支持
                await NotifyAsyncSafeAsync(new JsonObject {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = null
                }).ConfigureAwait(false);
            }
        }
    }

    private async Task NotifyAsyncSafeAsync(JsonNode message) {
        try {
            await WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
        } catch (Exception) {
            // 服务器已退出
        }
    }

    private static async Task<byte[]?> ReadMessageAsync(Stream stream, CancellationToken ct) {
        var header = new StringBuilder();
        var one = new byte[1];
        while (true) {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0) return null;
            header.Append((char)one[0]);
            if (header.Length >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n') break;
            if (header.Length > 8192) throw new InvalidDataException("LSP 消息头超长");
        }

        var length = -1;
        foreach (var line in header.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries)) {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                length = parsed;
            }
        }
        if (length < 0) throw new InvalidDataException("LSP 消息缺少 Content-Length");

        var body = new byte[length];
        var offset = 0;
        while (offset < length) {
            var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read == 0) return null;
            offset += read;
        }
        return body;
    }

    private void FailAllPending() {
        foreach (var (_, pending) in _pending) pending.TrySetResult(null);
        _pending.Clear();
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RequestAsync("shutdown", null, cts.Token).ConfigureAwait(false);
            await NotifyAsync("exit", null, cts.Token).ConfigureAwait(false);
        } catch (Exception) {
            // 服务器已经不可用
        }
        try {
            _readCts.Cancel();
            if (!_process.HasExited) {
                if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
            }
        } catch (Exception) {
            // 已退出
        }
        _process.Dispose();
        _writeLock.Dispose();
        _readCts.Dispose();
    }
}
