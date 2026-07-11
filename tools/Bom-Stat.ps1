<#
.SYNOPSIS
  统计 .cs 文件的 BOM 分布按目录分类.
#>

[CmdletBinding()]
param(
    [string]$Path = "."
)

$root = (Get-Location).Path
# 排除常见临时目录: obj/ bin/ .git/ .vs/ node_modules/
$excludeDirs = @('obj', 'bin', '.git', '.vs', 'node_modules')
$csFiles = Get-ChildItem -Path $Path -Recurse -Filter *.cs -File |
    Where-Object { $dir = $_.DirectoryName.Substring($root.Length + 1); -not ($excludeDirs | Where-Object { $dir -match "[\\/]$_\b" -or $dir -match "[\\/]$_$" }) }

$grouped = @{}
$totalBomsByDir = @{}  # 详细: 一级目录/二级目录
foreach ($file in $csFiles) {
    $relPath = $file.FullName.Substring($root.Length + 1)
    $parts = $relPath -split '\\'
    $topDir = $parts[0]
    $subDir = if ($parts.Length -gt 1) { "$topDir/$($parts[1])" } else { $topDir }

    if (-not $grouped.ContainsKey($topDir)) {
        $grouped[$topDir] = @{ Total = 0; Bom = 0 }
    }
    $grouped[$topDir].Total++

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $grouped[$topDir].Bom++

        if (-not $totalBomsByDir.ContainsKey($subDir)) {
            $totalBomsByDir[$subDir] = 0
        }
        $totalBomsByDir[$subDir]++
    }
}

Write-Host ""
Write-Host "====== BOM 一级目录统计 ======" -ForegroundColor Cyan
Write-Host ("{0,-30} {1,10} {2,10} {3,10}" -f "目录", "总文件", "BOM", "百分比")
Write-Host ("-" * 65)

$totalFiles = 0
$totalBom = 0
foreach ($key in ($grouped.Keys | Sort-Object)) {
    $info = $grouped[$key]
    $pct = if ($info.Total -gt 0) { [math]::Round($info.Bom * 100.0 / $info.Total, 1) } else { 0 }
    Write-Host ("{0,-30} {1,10} {2,10} {3,10}%" -f $key, $info.Total, $info.Bom, $pct)
    $totalFiles += $info.Total
    $totalBom += $info.Bom
}

Write-Host ("-" * 65)
$totalPct = if ($totalFiles -gt 0) { [math]::Round($totalBom * 100.0 / $totalFiles, 1) } else { 0 }
Write-Host ("{0,-30} {1,10} {2,10} {3,10}%" -f "总计", $totalFiles, $totalBom, $totalPct) -ForegroundColor Yellow

Write-Host ""
Write-Host "====== BOM 二级目录详细 (Top 20) ======" -ForegroundColor Cyan
$totalBomsByDir.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Host ("  {0,-40} {1,8}" -f $_.Key, $_.Value)
}
