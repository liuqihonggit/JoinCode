Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile('D:\project\w1\tool\screenshot_activity_bar.png')
# 缩小到 50%
$nw = [int]($src.Width * 0.5)
$nh = [int]($src.Height * 0.5)
$small = New-Object System.Drawing.Bitmap $nw, $nh
$g = [System.Drawing.Graphics]::FromImage($small)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src, 0, 0, $nw, $nh)
$small.Save('D:\project\w1\tool\screenshot_small.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$small.Dispose()
$src.Dispose()
Write-Output "Small saved: ${nw}x${nh}"
