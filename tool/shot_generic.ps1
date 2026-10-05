param([string]$ExePath, [string]$ProcName, [int]$Wait=5)
Get-Process $ProcName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-Process $ExePath
Write-Output "Started $ProcName, waiting ${Wait}s..."
Start-Sleep -Seconds $Wait
$p = Get-Process $ProcName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "$ProcName not running"; exit }
Write-Output "$ProcName running: PID=$($p.Id) Title='$($p.MainWindowTitle)'"
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Shot5 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
# SW_RESTORE=9, SW_SHOW=5
[Win32Shot5]::ShowWindowAsync($p.MainWindowHandle, 9) | Out-Null
Start-Sleep -Milliseconds 500
[Win32Shot5]::ShowWindow($p.MainWindowHandle, 5) | Out-Null
Start-Sleep -Milliseconds 300
[Win32Shot5]::BringWindowToTop($p.MainWindowHandle) | Out-Null
[Win32Shot5]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1000
# Retry foreground
[Win32Shot5]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800
$r = New-Object Win32Shot5+RECT
[Win32Shot5]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) { Write-Output "Invalid rect: ${w}x${ht}"; exit }
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$out = "D:\project\w1\tool\${ProcName}_$ts.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SCREENSHOT_PATH=$out"
Write-Output "Size: ${w}x${ht}"
