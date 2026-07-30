namespace Core.Security.Sandbox.Native;

using System.Runtime.InteropServices;

internal static class JobObjectNative
{
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    internal const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x0008;
    internal const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x0200;

    internal const int JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 0x0009;

    internal const uint PROCESS_TERMINATE = 0x0001;
    internal const uint PROCESS_SET_QUOTA = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateJobObjectW(nint lpJobAttributes, nint lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nint MinimumWorkingSetSize;
        internal nint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        internal long ReadOperationCount;
        internal long WriteOperationCount;
        internal long OtherOperationCount;
        internal long ReadTransferCount;
        internal long WriteTransferCount;
        internal long OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        internal JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        internal IO_COUNTERS IoInfo;
        internal nint ProcessMemoryLimit;
        internal nint JobMemoryLimit;
        internal nint PeakProcessMemoryUsed;
        internal nint PeakJobMemoryUsed;
    }
}
