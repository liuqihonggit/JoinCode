Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile('D:\project\w1\tool\screenshot_v2.png')
# 裁剪左侧 120px 宽
$crop = New-Object System.Drawing.Bitmap 120, $src.Height
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, 120, $src.Height), 0, 0, 120, $src.Height, [System.Drawing.GraphicsUnit]::Pixel)
$nh = [int]($crop.Height * 0.6)
$small = New-Object System.Drawing.Bitmap 120, $nh
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 120, $nh)
$small.Save('D:\project\w1\tool\screenshot_v2_crop.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$g2.Dispose()
$crop.Dispose()
$small.Dispose()
$src.Dispose()
Write-Output "Saved"
