<#
.SYNOPSIS
  批量去除 C# 文件的 UTF-8 BOM 编码 (字节级强制).
.DESCRIPTION
  通过直接字节操作去除 UTF-8 BOM (EF BB BF), 不经过文本解码.
  适用场景: 之前 ReadAllText/WriteAllText 操作被 dotnet build 还原.
#>

[CmdletBinding()]
param(
    [string]$Path = "src/hosts/JoinCode/ChatCommands",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path

if (-not (Test-Path $Path)) {
    Write-Error "目录不存在: $Path"
    exit 1
}

Write-Host "扫描目录: $Path" -ForegroundColor Cyan
Write-Host "模式: $(if ($DryRun) {'预览 (DryRun)'} else {'处理 + 写入'})" -ForegroundColor Cyan
Write-Host ""

$excludeDirs = @('obj', 'bin', '.git', '.vs', 'node_modules')
$csFiles = Get-ChildItem -Path $Path -Recurse -Filter *.cs -File |
    Where-Object { $dir = $_.DirectoryName.Substring($root.Length + 1); -not ($excludeDirs | Where-Object { $dir -match "[\\/]$_\b" -or $dir -match "[\\/]$_$" }) }
Write-Host "找到 .cs 文件: $($csFiles.Count) 个" -ForegroundColor Gray
Write-Host ""

$bomCount = 0
$processed = 0
$skipped = 0
$errors = 0

foreach ($file in $csFiles) {
    $relPath = $file.FullName.Substring($root.Length + 1)
    try {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

        # 检测 UTF-8 BOM (EF BB BF)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $bomCount++
            if ($DryRun) {
                Write-Host "[BOM]    $relPath" -ForegroundColor Yellow
            } else {
                # 字节级: 直接砍掉前 3 字节, 写回
                $newBytes = $bytes[3..($bytes.Length - 1)]
                [System.IO.File]::WriteAllBytes($file.FullName, $newBytes)
                $processed++
            }
        } else {
            $skipped++
        }
    } catch {
        Write-Host "[ERROR]  $relPath - $_" -ForegroundColor Red
        $errors++
    }
}

Write-Host ""
Write-Host "========== 完成 ==========" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "预览完成: 找到 BOM 文件 $bomCount 个 (无修改)" -ForegroundColor Yellow
} else {
    Write-Host "处理完成: 去除 BOM $processed 个, 跳过(无 BOM) $skipped 个, 错误 $errors 个" -ForegroundColor $(if ($errors -gt 0) {'Red'} else {'Green'})
}

if ($errors -gt 0) { exit 1 }
