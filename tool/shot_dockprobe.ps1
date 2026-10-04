Get-Process 'DockProbe' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-Process "D:\project\w1\artifacts\bin\DockProbe\Debug\net10.0\DockProbe.exe"
Write-Output "Started DockProbe, waiting 5s..."
Start-Sleep -Seconds 5

$p = Get-Process 'DockProbe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "DockProbe not running"; exit }
Write-Output "DockProbe running: PID=$($p.Id)"

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Shot3 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
[Win32Shot3]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
Start-Sleep -Milliseconds 300
[Win32Shot3]::BringWindowToTop($p.MainWindowHandle) | Out-Null
[Win32Shot3]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

$r = New-Object Win32Shot3+RECT
[Win32Shot3]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) { Write-Output "Invalid rect: ${w}x${ht}"; exit }
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$out = "D:\project\w1\tool\dockprobe_$ts.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output "SCREENSHOT_PATH=$out"
Write-Output "Size: ${w}x${ht}"
