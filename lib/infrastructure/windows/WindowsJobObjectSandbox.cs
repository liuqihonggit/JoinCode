namespace Infrastructure.Windows.JobObject;

/// <summary>
/// Windows JobObject 沙箱 — 通过 Windows Job Object 限制子进程内存、CPU 与进程数,并在句柄关闭时自动终止所有子进程
/// <para>仅支持 Windows 平台,非 Windows 调用将抛出 PlatformNotSupportedException</para>
/// </summary>
public sealed class WindowsJobObjectSandbox : IDisposable {
    private nint _jobHandle;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// 构造 JobObject 沙箱
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public WindowsJobObjectSandbox(ILogger? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 创建 JobObject 并设置资源限制
    /// </summary>
    /// <param name="memoryLimitBytes">单进程内存上限(字节),null 表示不限制</param>
    /// <param name="cpuLimitPercent">CPU 占用百分比上限,null 表示不限制(当前未实现)</param>
    /// <param name="activeProcessLimit">活动进程数上限,null 表示不限制</param>
    /// <returns>JobObject 句柄</returns>
    /// <exception cref="PlatformNotSupportedException">非 Windows 平台</exception>
    /// <exception cref="InvalidOperationException">创建或设置限制失败</exception>
    public nint CreateJobObject(long? memoryLimitBytes = null, int? cpuLimitPercent = null, int? activeProcessLimit = null) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("[WIN001] Windows JobObject 仅支持 Windows 平台");
        }

        _jobHandle = JobObjectNative.CreateJobObjectW(nint.Zero, nint.Zero);
        if (_jobHandle == nint.Zero) {
            var error = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"[INF056] 创建 JobObject 失败, Win32 错误码: {error}");
        }

        var info = new JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        uint limitFlags = JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (memoryLimitBytes.HasValue && memoryLimitBytes.Value > 0) {
            limitFlags |= JobObjectNative.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            info.ProcessMemoryLimit = (nint)memoryLimitBytes.Value;
        }

        if (activeProcessLimit.HasValue && activeProcessLimit.Value > 0) {
            limitFlags |= JobObjectNative.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            info.BasicLimitInformation.ActiveProcessLimit = (uint)activeProcessLimit.Value;
        }

        info.BasicLimitInformation.LimitFlags = limitFlags;

        var success = JobObjectNative.SetInformationJobObject(
            _jobHandle,
            JobObjectNative.JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
            ref info,
            (uint)Marshal.SizeOf<JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());

        if (!success && limitFlags != JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 设置限制失败(错误码: {Error})，降级为仅 KILL_ON_JOB_CLOSE", error);

            info = new JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            success = JobObjectNative.SetInformationJobObject(
                _jobHandle,
                JobObjectNative.JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
                ref info,
                (uint)Marshal.SizeOf<JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        }

        if (!success) {
            var error = Marshal.GetLastPInvokeError();
            CloseHandle();
            throw new InvalidOperationException($"[INF057] 设置 JobObject 限制失败, Win32 错误码: {error}");
        }

        _logger?.LogInformation("[WindowsJobObject] JobObject 已创建, Handle: {Handle}, 限制: {Flags}", _jobHandle, limitFlags);

        return _jobHandle;
    }

    /// <summary>
    /// 将进程分配到当前 JobObject
    /// </summary>
    /// <param name="processId">进程 ID</param>
    /// <returns>分配成功返回 true;非 Windows、句柄未创建或打开进程失败返回 false</returns>
    public bool AssignProcess(int processId) {
        if (!OperatingSystem.IsWindows()) {
            return false;
        }

        if (_jobHandle == nint.Zero) {
            throw new InvalidOperationException("[WIN002] JobObject 未创建");
        }

        using var processHandle = new SafeProcessHandle(
            JobObjectNative.OpenProcess(
                JobObjectNative.PROCESS_TERMINATE | JobObjectNative.PROCESS_SET_QUOTA,
                false,
                processId),
            ownsHandle: true);

        if (processHandle.IsInvalid) {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 打开进程 {Pid} 失败, Win32 错误码: {Error}", processId, error);
            return false;
        }

        var success = JobObjectNative.AssignProcessToJobObject(_jobHandle, processHandle.DangerousGetHandle());
        if (!success) {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 将进程 {Pid} 分配到 JobObject 失败, Win32 错误码: {Error}", processId, error);
            return false;
        }

        _logger?.LogInformation("[WindowsJobObject] 进程 {Pid} 已分配到 JobObject", processId);
        return true;
    }

    /// <summary>
    /// 终止 JobObject 中所有进程
    /// </summary>
    /// <param name="exitCode">进程退出码,默认 1</param>
    /// <returns>终止成功返回 true;非 Windows 或句柄未创建返回 false</returns>
    public bool TerminateAllProcesses(uint exitCode = 1) {
        if (!OperatingSystem.IsWindows() || _jobHandle == nint.Zero) {
            return false;
        }

        var success = JobObjectNative.TerminateProcess(_jobHandle, exitCode);
        if (!success) {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 终止 JobObject 中所有进程失败, Win32 错误码: {Error}", error);
        }

        return success;
    }

    private void CloseHandle() {
        if (_jobHandle != nint.Zero && OperatingSystem.IsWindows()) {
            JobObjectNative.CloseHandle(_jobHandle);
            _jobHandle = nint.Zero;
        }
    }

    /// <summary>释放沙箱 — 关闭 JobObject 句柄,所有子进程将被 KILL_ON_JOB_CLOSE 终止</summary>
    public void Dispose() {
        if (_disposed) return; _disposed = true;
        CloseHandle();
    }
}