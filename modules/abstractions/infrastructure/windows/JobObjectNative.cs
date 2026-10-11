namespace Infrastructure.Windows.JobObject;

/// <summary>
/// Windows Job Object 原生 API P/Invoke 声明 — 用于进程组限制与终止
/// </summary>
public static class JobObjectNative {
    /// <summary>Job 关闭时终止所有关联进程的限制标志</summary>
    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    /// <summary>限制 Job 内活动进程数的限制标志</summary>
    public const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x0008;
    /// <summary>限制 Job 内进程内存的限制标志</summary>
    public const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x0200;

    /// <summary>Job Object 扩展限制信息的信息类标识</summary>
    public const int JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 0x0009;

    /// <summary>进程终止权限</summary>
    public const uint PROCESS_TERMINATE = 0x0001;
    /// <summary>进程设置配额权限</summary>
    public const uint PROCESS_SET_QUOTA = 0x0010;

    /// <summary>创建 Job Object</summary>
    /// <param name="lpJobAttributes">安全属性，可为零</param>
    /// <param name="lpName">Job 名称，可为零</param>
    /// <returns>Job Object 句柄</returns>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateJobObjectW(nint lpJobAttributes, nint lpName);

    /// <summary>设置 Job Object 信息</summary>
    /// <param name="hJob">Job Object 句柄</param>
    /// <param name="JobObjectInformationClass">信息类标识</param>
    /// <param name="lpJobObjectInformation">信息结构体引用</param>
    /// <param name="cbJobObjectInformationLength">信息结构体长度</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    /// <summary>将进程分配到 Job Object</summary>
    /// <param name="hJob">Job Object 句柄</param>
    /// <param name="hProcess">进程句柄</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    /// <summary>关闭内核对象句柄</summary>
    /// <param name="hObject">对象句柄</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);

    /// <summary>打开指定进程获取句柄</summary>
    /// <param name="dwDesiredAccess">访问权限</param>
    /// <param name="bInheritHandle">子进程是否继承句柄</param>
    /// <param name="dwProcessId">进程 ID</param>
    /// <returns>进程句柄</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    /// <summary>终止指定进程</summary>
    /// <param name="hProcess">进程句柄</param>
    /// <param name="uExitCode">退出码</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    /// <summary>Job Object 基本限制信息</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
        /// <summary>每进程用户态时间限制</summary>
        public long PerProcessUserTimeLimit;
        /// <summary>每 Job 用户态时间限制</summary>
        public long PerJobUserTimeLimit;
        /// <summary>限制标志位</summary>
        public uint LimitFlags;
        /// <summary>最小工作集大小</summary>
        public nint MinimumWorkingSetSize;
        /// <summary>最大工作集大小</summary>
        public nint MaximumWorkingSetSize;
        /// <summary>活动进程数上限</summary>
        public uint ActiveProcessLimit;
        /// <summary>CPU 亲和性</summary>
        public nint Affinity;
        /// <summary>优先级类</summary>
        public uint PriorityClass;
        /// <summary>调度类</summary>
        public uint SchedulingClass;
    }

    /// <summary>IO 计数器</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS {
        /// <summary>读操作计数</summary>
        public long ReadOperationCount;
        /// <summary>写操作计数</summary>
        public long WriteOperationCount;
        /// <summary>其他操作计数</summary>
        public long OtherOperationCount;
        /// <summary>读传输字节数</summary>
        public long ReadTransferCount;
        /// <summary>写传输字节数</summary>
        public long WriteTransferCount;
        /// <summary>其他传输字节数</summary>
        public long OtherTransferCount;
    }

    /// <summary>Job Object 扩展限制信息</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        /// <summary>基本限制信息</summary>
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        /// <summary>IO 计数器</summary>
        public IO_COUNTERS IoInfo;
        /// <summary>单进程内存上限</summary>
        public nint ProcessMemoryLimit;
        /// <summary>Job 内存上限</summary>
        public nint JobMemoryLimit;
        /// <summary>单进程峰值内存使用</summary>
        public nint PeakProcessMemoryUsed;
        /// <summary>Job 峰值内存使用</summary>
        public nint PeakJobMemoryUsed;
    }
}