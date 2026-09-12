namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Native DLL 插件 ABI 契约 — C ABI 入口点名称 + 错误码 + 常量 (ADR 0099)
/// <para>插件用 NativeAOT 编译为 native .dll，导出以下 C 函数:</para>
/// <list type="bullet">
/// <item>plugin_load(config_ptr, config_len) → int32</item>
/// <item>plugin_invoke(req_ptr, req_len, resp_ptr, resp_cap) → int32</item>
/// <item>plugin_unload() → int32</item>
/// <item>plugin_info(resp_ptr, resp_cap) → int32 (可选)</item>
/// </list>
/// </summary>
public static class NativePluginAbi
{
    /// <summary>加载入口 — 传入 JSON 配置</summary>
    public const string EntryLoad = "plugin_load";

    /// <summary>调用入口 — 传入 JSON 请求，写 JSON 响应</summary>
    public const string EntryInvoke = "plugin_invoke";

    /// <summary>卸载入口 — 清理插件资源</summary>
    public const string EntryUnload = "plugin_unload";

    /// <summary>元数据入口 — 返回插件信息 JSON (可选)</summary>
    public const string EntryInfo = "plugin_info";

    /// <summary>默认响应缓冲区容量 (64KB)</summary>
    public const int DefaultResponseCapacity = 64 * 1024;

    /// <summary>最大响应缓冲区容量 (4MB — 超过此值应改用 shared memory)</summary>
    public const int MaxResponseCapacity = 4 * 1024 * 1024;
}

/// <summary>
/// Native 插件调用错误码 — plugin_invoke 返回负数时的具体含义
/// </summary>
public enum NativePluginError : int
{
    /// <summary>成功</summary>
    Ok = 0,

    /// <summary>通用错误</summary>
    Generic = -1,

    /// <summary>响应缓冲区不够 — 宿主应重试更大 buffer</summary>
    BufferTooSmall = -2,

    /// <summary>方法未找到</summary>
    MethodNotFound = -3,

    /// <summary>JSON 反序列化失败</summary>
    BadRequest = -4,

    /// <summary>插件未加载</summary>
    NotLoaded = -5,

    /// <summary>内部异常</summary>
    InternalError = -6,
}

/// <summary>
/// Native 插件加载结果
/// </summary>
public readonly struct NativePluginLoadResult
{
    /// <summary>是否成功</summary>
    public bool IsSuccess { get; }

    /// <summary>错误码 (失败时)</summary>
    public NativePluginError Error { get; }

    /// <summary>错误消息 (失败时)</summary>
    public string? ErrorMessage { get; }

    private NativePluginLoadResult(bool isSuccess, NativePluginError error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>成功</summary>
    public static NativePluginLoadResult Ok() => new(true, NativePluginError.Ok, null);

    /// <summary>失败</summary>
    public static NativePluginLoadResult Fail(NativePluginError error, string? message = null)
        => new(false, error, message);
}

/// <summary>
/// Native 插件调用结果 — 成功返回 JSON 响应，失败返回错误码
/// </summary>
public readonly struct NativePluginInvokeResult
{
    /// <summary>是否成功</summary>
    public bool IsSuccess { get; }

    /// <summary>JSON 响应 (成功时)</summary>
    public string? ResponseJson { get; }

    /// <summary>错误码 (失败时)</summary>
    public NativePluginError Error { get; }

    /// <summary>错误消息 (失败时)</summary>
    public string? ErrorMessage { get; }

    private NativePluginInvokeResult(bool isSuccess, string? responseJson, NativePluginError error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ResponseJson = responseJson;
        Error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>成功</summary>
    public static NativePluginInvokeResult Ok(string responseJson)
        => new(true, responseJson, NativePluginError.Ok, null);

    /// <summary>失败</summary>
    public static NativePluginInvokeResult Fail(NativePluginError error, string? message = null)
        => new(false, null, error, message);
}
