Start-Sleep -Seconds 2
Add-Type -AssemblyName System.Drawing
$p = Get-Process 'JoinCode.Gui' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output 'GUI not running'; exit }
$h = $p.MainWindowHandle
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32Helper2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$r = New-Object Win32Helper2+RECT
[Win32Helper2]::GetWindowRect($h, [ref]$r) | Out-Null
[Win32Helper2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save('D:\project\w1\tool\screenshot_activity_bar.png')
$g.Dispose()
$bmp.Dispose()
Write-Output "Screenshot saved: ${w}x${ht}"
