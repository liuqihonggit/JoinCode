param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Page,
    [Parameter(Mandatory = $true)][string]$Result
)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeClick {
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    public static string ClickClientCenter(IntPtr hWnd) {
        RECT r;
        if (!GetClientRect(hWnd, out r)) return "GetClientRect failed";
        var pt = new POINT { X = (r.Right - r.Left) / 2, Y = (r.Bottom - r.Top) / 2 };
        if (!ClientToScreen(hWnd, ref pt)) return "ClientToScreen failed";
        SetForegroundWindow(hWnd);
        SetCursorPos(pt.X, pt.Y);
        System.Threading.Thread.Sleep(200);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        return string.Format("client {0}x{1} center -> screen ({2}, {3})", r.Right - r.Left, r.Bottom - r.Top, pt.X, pt.Y);
    }
}
"@

Remove-Item -LiteralPath $Result -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$Result.ready" -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $Exe -ArgumentList '--input-probe', '--page', $Page, '--result', $Result, '--timeout', '20' -PassThru
Write-Output "started pid=$($p.Id)"

$deadline = (Get-Date).AddSeconds(15)
while (-not (Test-Path -LiteralPath "$Result.ready") -and (Get-Date) -lt $deadline) {
    if ($p.HasExited) { throw "process exited early code=$($p.ExitCode)" }
    Start-Sleep -Milliseconds 200
}
if (-not (Test-Path -LiteralPath "$Result.ready")) { $p.Kill(); throw 'probe-armed marker not found' }
Write-Output 'probe armed (.ready marker seen)'

$p.Refresh()
Write-Output ([NativeClick]::ClickClientCenter([IntPtr]$p.MainWindowHandle))

$done = $p.WaitForExit(15000)
Write-Output "processExited=$done exitCode=$(if ($done) { $p.ExitCode } else { 'TIMEOUT' })"
if (-not $done) { $p.Kill() }
if (Test-Path -LiteralPath $Result) { Get-Content -LiteralPath $Result } else { Write-Output 'result file missing' }
