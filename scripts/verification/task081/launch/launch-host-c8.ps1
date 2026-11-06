$root = Resolve-Path "$PSScriptRoot\..\..\..\.."
$cmd = "cmd.exe /c `"$root\scripts\verification\task081\launch\launch-host-c8.cmd`""
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd } |
    Select-Object ProcessId, ReturnValue | Format-List
