Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile('D:\project\w1\tool\screenshot_activity_bar.png')
# 裁剪左侧 60px 宽（ActivityBar 区域）
$crop = New-Object System.Drawing.Bitmap 60, $src.Height
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, 60, $src.Height), 0, 0, 60, $src.Height, [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save('D:\project\w1\tool\screenshot_activity_bar_crop.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$crop.Dispose()
$src.Dispose()
Write-Output "Cropped saved"
