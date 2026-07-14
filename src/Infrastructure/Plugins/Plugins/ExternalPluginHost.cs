
namespace Core.Plugins;

/// <summary>
/// 外部exe进程插件宿主 - 管理独立进程的生命周期
/// </summary>
public sealed class ExternalPluginHost : IDisposable
{
    private readonly Process _process;
    private readonly string _pluginName;
    private readonly ILogger? _logger;
    private bool _isDisposed;
    private bool _isUnloaded;

    public string PluginName => _pluginName;
    public int ProcessId => _process.Id;
    public bool IsRunning => !_process.HasExited;
    public string ExePath { get; }

    public ExternalPluginHost(string pluginName, Process process, string exePath, ILogger? logger = null)
    {
        _pluginName = pluginName;
        _process = process;
        ExePath = exePath;
        _logger = logger;
    }

    /// <summary>
    /// 向外部插件进程发送消息（通过stdin）
    /// </summary>
    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_process.HasExited)
        {
            throw new InvalidOperationException($"外部插件 '{_pluginName}' 进程已退出");
        }

        try
        {
            await _process.StandardInput.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "向外部插件 {PluginName} 发送消息失败", _pluginName);
            throw;
        }
    }

    /// <summary>
    /// 从外部插件进程读取消息（从stdout）
    /// </summary>
    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_process.HasExited)
        {
            return null;
        }

        try
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            return line;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "从外部插件 {PluginName} 读取消息失败", _pluginName);
            return null;
        }
    }

    /// <summary>
    /// 请求外部插件停止
    /// </summary>
    public PluginUnloadResult Unload()
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_isUnloaded)
        {
            return PluginUnloadResult.AlreadyUnloaded(_pluginName);
        }

        _isUnloaded = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();

                if (!_process.WaitForExit(5000))
                {
                    _process.Kill();
                    _logger?.LogWarning("外部插件 {PluginName} 未在超时内退出，已强制终止", _pluginName);
                }
            }

            _logger?.LogInformation("外部插件 {PluginName} 已停止", _pluginName);
            return PluginUnloadResult.Success(_pluginName, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止外部插件 {PluginName} 时发生异常", _pluginName);
            return PluginUnloadResult.AlcUnloadFailed(_pluginName, TimeSpan.Zero, ex.Message);
        }
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed)) return;

        if (!_isUnloaded)
        {
            Unload();
        }

        try
        {
            _process.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"ExternalPluginHost: failed to close process: {ex.Message}");
        }
    }
}
