$cmd = 'cmd.exe /c D:\project\w3\.xxx\launch-host-c8.cmd'
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd } |
    Select-Object ProcessId, ReturnValue | Format-List
