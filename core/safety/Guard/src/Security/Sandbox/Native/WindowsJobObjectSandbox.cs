namespace Core.Security.Sandbox.Native;

using System.Runtime.InteropServices;

internal sealed class WindowsJobObjectSandbox : IDisposable
{
    private nint _jobHandle;
    private readonly ILogger? _logger;

    public WindowsJobObjectSandbox(ILogger? logger = null)
    {
        _logger = logger;
    }

    public nint CreateJobObject(long? memoryLimitBytes = null, int? cpuLimitPercent = null, int? activeProcessLimit = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows JobObject 仅支持 Windows 平台");
        }

        _jobHandle = JobObjectNative.CreateJobObjectW(nint.Zero, nint.Zero);
        if (_jobHandle == nint.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"创建 JobObject 失败, Win32 错误码: {error}");
        }

        var info = new JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        uint limitFlags = JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (memoryLimitBytes.HasValue && memoryLimitBytes.Value > 0)
        {
            limitFlags |= JobObjectNative.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            info.ProcessMemoryLimit = (nint)memoryLimitBytes.Value;
        }

        if (activeProcessLimit.HasValue && activeProcessLimit.Value > 0)
        {
            limitFlags |= JobObjectNative.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            info.BasicLimitInformation.ActiveProcessLimit = (uint)activeProcessLimit.Value;
        }

        info.BasicLimitInformation.LimitFlags = limitFlags;

        var success = JobObjectNative.SetInformationJobObject(
            _jobHandle,
            JobObjectNative.JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
            ref info,
            (uint)Marshal.SizeOf<JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());

        if (!success && limitFlags != JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)
        {
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

        if (!success)
        {
            var error = Marshal.GetLastPInvokeError();
            CloseHandle();
            throw new InvalidOperationException($"设置 JobObject 限制失败, Win32 错误码: {error}");
        }

        _logger?.LogInformation("[WindowsJobObject] JobObject 已创建, Handle: {Handle}, 限制: {Flags}", _jobHandle, limitFlags);

        return _jobHandle;
    }

    public bool AssignProcess(int processId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (_jobHandle == nint.Zero)
        {
            throw new InvalidOperationException("JobObject 未创建");
        }

        var processHandle = JobObjectNative.OpenProcess(
            JobObjectNative.PROCESS_TERMINATE | JobObjectNative.PROCESS_SET_QUOTA,
            false,
            processId);

        if (processHandle == nint.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 打开进程 {Pid} 失败, Win32 错误码: {Error}", processId, error);
            return false;
        }

        try
        {
            var success = JobObjectNative.AssignProcessToJobObject(_jobHandle, processHandle);
            if (!success)
            {
                var error = Marshal.GetLastPInvokeError();
                _logger?.LogWarning("[WindowsJobObject] 将进程 {Pid} 分配到 JobObject 失败, Win32 错误码: {Error}", processId, error);
                return false;
            }

            _logger?.LogInformation("[WindowsJobObject] 进程 {Pid} 已分配到 JobObject", processId);
            return true;
        }
        finally
        {
            JobObjectNative.CloseHandle(processHandle);
        }
    }

    public bool TerminateAllProcesses(uint exitCode = 1)
    {
        if (!OperatingSystem.IsWindows() || _jobHandle == nint.Zero)
        {
            return false;
        }

        var success = JobObjectNative.TerminateProcess(_jobHandle, exitCode);
        if (!success)
        {
            var error = Marshal.GetLastPInvokeError();
            _logger?.LogWarning("[WindowsJobObject] 终止 JobObject 中所有进程失败, Win32 错误码: {Error}", error);
        }

        return success;
    }

    private void CloseHandle()
    {
        if (_jobHandle != nint.Zero && OperatingSystem.IsWindows())
        {
            JobObjectNative.CloseHandle(_jobHandle);
            _jobHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        CloseHandle();
    }
}
