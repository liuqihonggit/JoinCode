Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile('D:\project\w1\tool\screenshot_v2.png')
# 裁剪左侧 260px 宽
$crop = New-Object System.Drawing.Bitmap 260, $src.Height
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, 260, $src.Height), 0, 0, 260, $src.Height, [System.Drawing.GraphicsUnit]::Pixel)
$nh = [int]($crop.Height * 0.5)
$small = New-Object System.Drawing.Bitmap 260, $nh
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 260, $nh)
$small.Save('D:\project\w1\tool\screenshot_wide.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$g2.Dispose()
$crop.Dispose()
$small.Dispose()
$src.Dispose()
Write-Output "Saved"
