namespace Core.Plugins;

/// <summary>
/// Native DLL 插件宿主 — 管理 native DLL 插件的生命周期 (ADR 0099)
/// <para>加载: NativeLibrary.Load + GetExport 获取 plugin_load/invoke/unload 函数指针</para>
/// <para>调用: JSON 请求 → UTF-8 bytes → pinned buffer → plugin_invoke → 读响应</para>
/// <para>卸载: plugin_unload + NativeLibrary.Free</para>
/// <para>线程安全: 非线程安全,调用方需自行同步(Actor 模式下单线程访问)</para>
/// </summary>
public sealed unsafe class NativePluginHost : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PluginLoadDelegate(byte* configPtr, int configLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PluginInvokeDelegate(byte* reqPtr, int reqLen, byte* respPtr, int respCap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PluginUnloadDelegate();

    private IntPtr _handle;
    private readonly string _pluginPath;
    private readonly string _pluginName;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;

    private PluginLoadDelegate? _loadFn;
    private PluginInvokeDelegate? _invokeFn;
    private PluginUnloadDelegate? _unloadFn;

    private bool _isLoaded;
    private bool _isDisposed;

    /// <summary>插件名称</summary>
    public string PluginName => _pluginName;

    /// <summary>DLL 路径</summary>
    public string PluginPath => _pluginPath;

    /// <summary>是否已加载</summary>
    public bool IsLoaded => _isLoaded && !_isDisposed;

    /// <summary>
    /// 构造 — 指定 native DLL 路径和插件名
    /// </summary>
    /// <param name="pluginPath">native DLL 绝对路径</param>
    /// <param name="pluginName">插件名(唯一标识)</param>
    /// <param name="fs">文件系统抽象(用于检查 DLL 是否存在)</param>
    /// <param name="logger">可选日志</param>
    public NativePluginHost(string pluginPath, string pluginName, IFileSystem fs, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(fs);
        _pluginPath = pluginPath;
        _pluginName = pluginName;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 加载插件 — NativeLibrary.Load + GetExport + plugin_load
    /// <para>重复加载返回成功(幂等)</para>
    /// </summary>
    /// <param name="configJson">JSON 配置(传给 plugin_load)</param>
    /// <returns>加载结果</returns>
    public NativePluginLoadResult Load(string? configJson = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isLoaded) return NativePluginLoadResult.Ok();

        if (!_fs.FileExists(_pluginPath))
            return NativePluginLoadResult.Fail(NativePluginError.NotLoaded, $"DLL 不存在: {_pluginPath}");

        _handle = NativeLibrary.Load(_pluginPath);
        if (_handle == IntPtr.Zero)
            return NativePluginLoadResult.Fail(NativePluginError.NotLoaded, $"NativeLibrary.Load 失败: {_pluginPath}");

        if (!TryGetExports())
        {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
            return NativePluginLoadResult.Fail(NativePluginError.Generic, $"缺少必要导出函数 ({NativePluginAbi.EntryLoad}/{NativePluginAbi.EntryInvoke}/{NativePluginAbi.EntryUnload})");
        }

        var loadResult = CallLoad(configJson);
        if (loadResult != 0)
        {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
            return NativePluginLoadResult.Fail((NativePluginError)loadResult, $"plugin_load 返回错误码 {loadResult}");
        }

        _isLoaded = true;
        _logger?.LogInformation("[NativePlugin] {Name} 已加载 from {Path}", _pluginName, _pluginPath);
        return NativePluginLoadResult.Ok();
    }

    /// <summary>
    /// 调用插件方法 — JSON 请求 → plugin_invoke → JSON 响应
    /// <para>BufferTooSmall 时自动重试更大 buffer(最多 4MB)</para>
    /// </summary>
    /// <param name="requestJson">JSON 请求</param>
    /// <param name="responseCapacity">响应缓冲区初始容量(默认 64KB)</param>
    /// <returns>调用结果</returns>
    public NativePluginInvokeResult Invoke(string requestJson, int responseCapacity = NativePluginAbi.DefaultResponseCapacity)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_isLoaded || _invokeFn is null)
            return NativePluginInvokeResult.Fail(NativePluginError.NotLoaded, "插件未加载");

        var reqBytes = Encoding.UTF8.GetBytes(requestJson);
        var cap = responseCapacity;
        if (cap < 256) cap = 256;

        while (cap <= NativePluginAbi.MaxResponseCapacity)
        {
            var respBytes = new byte[cap];
            int written;

            unsafe
            {
                fixed (byte* reqPtr = reqBytes)
                fixed (byte* respPtr = respBytes)
                {
                    written = _invokeFn(reqPtr, reqBytes.Length, respPtr, cap);
                }
            }

            if (written >= 0)
            {
                var responseJson = Encoding.UTF8.GetString(respBytes, 0, written);
                return NativePluginInvokeResult.Ok(responseJson);
            }

            if (written == (int)NativePluginError.BufferTooSmall)
            {
                cap *= 2;
                _logger?.LogDebug("[NativePlugin] {Name} 响应缓冲区不足,重试 {Cap} bytes", _pluginName, cap);
                continue;
            }

            var error = (NativePluginError)written;
            return NativePluginInvokeResult.Fail(error, $"plugin_invoke 返回错误码 {written}");
        }

        return NativePluginInvokeResult.Fail(NativePluginError.BufferTooSmall, $"响应超过最大容量 {NativePluginAbi.MaxResponseCapacity} bytes");
    }

    /// <summary>
    /// 卸载插件 — plugin_unload + NativeLibrary.Free
    /// <para>未加载时返回成功(幂等)</para>
    /// </summary>
    public void Unload()
    {
        if (_isDisposed || !_isLoaded) return;

        try
        {
            _unloadFn?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[NativePlugin] {Name} plugin_unload 抛异常", _pluginName);
        }

        NativeLibrary.Free(_handle);
        _handle = IntPtr.Zero;
        _isLoaded = false;
        _logger?.LogInformation("[NativePlugin] {Name} 已卸载", _pluginName);
    }

    /// <summary>析构 — 确保释放 native handle</summary>
    ~NativePluginHost() => Dispose(false);

    /// <summary>释放 — 卸载插件 + 释放 native handle</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        Unload();
        _isDisposed = true;
    }

    private bool TryGetExports()
    {
        var loadAddr = NativeLibrary.GetExport(_handle, NativePluginAbi.EntryLoad);
        var invokeAddr = NativeLibrary.GetExport(_handle, NativePluginAbi.EntryInvoke);
        var unloadAddr = NativeLibrary.GetExport(_handle, NativePluginAbi.EntryUnload);

        if (loadAddr == IntPtr.Zero || invokeAddr == IntPtr.Zero || unloadAddr == IntPtr.Zero)
            return false;

        _loadFn = Marshal.GetDelegateForFunctionPointer<PluginLoadDelegate>(loadAddr);
        _invokeFn = Marshal.GetDelegateForFunctionPointer<PluginInvokeDelegate>(invokeAddr);
        _unloadFn = Marshal.GetDelegateForFunctionPointer<PluginUnloadDelegate>(unloadAddr);
        return true;
    }

    private int CallLoad(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson) || _loadFn is null)
        {
            unsafe { return _loadFn?.Invoke(null, 0) ?? (int)NativePluginError.NotLoaded; }
        }

        var configBytes = Encoding.UTF8.GetBytes(configJson);
        unsafe
        {
            fixed (byte* configPtr = configBytes)
            {
                return _loadFn(configPtr, configBytes.Length);
            }
        }
    }
}
