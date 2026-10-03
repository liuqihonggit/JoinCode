# 杀掉所有 JoinCode 进程
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'JoinCode|jcc' } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Write-Output "Killed old processes"

# 启动 GUI
Start-Process "D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"
Write-Output "Started GUI, waiting 18s for engine load..."
Start-Sleep -Seconds 18

# 查找进程
$p = Get-Process 'JoinCode.Gui' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) {
  Write-Output "GUI not running after 18s"
  exit
}
Write-Output "GUI running: PID=$($p.Id), Title=$($p.MainWindowTitle)"

# 截图
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Shot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$r = New-Object Win32Shot+RECT
[Win32Shot]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
[Win32Shot]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) {
  Write-Output "Invalid window rect: ${w}x${ht}"
  exit
}
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save('D:\project\w1\tool\screenshot_v2.png')
$g.Dispose()
$bmp.Dispose()
Write-Output "Screenshot saved: ${w}x${ht}"
