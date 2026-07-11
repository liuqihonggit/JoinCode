# 定位 src/hosts 下所有 BOM 文件
$root = 'd:\jcc-w1\src\hosts'
$csFiles = Get-ChildItem -Path $root -Recurse -Include *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|\.git|\.vs|node_modules)\\' }

$results = @()
foreach ($file in $csFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $relPath = $file.FullName.Substring('d:\jcc-w1\'.Length)
        $results += $relPath
    }
}
Write-Host "====== BOM 文件清单 (src/hosts) ======"
Write-Host "总数: $($results.Count)"
foreach ($r in $results) { Write-Host "  $r" }
