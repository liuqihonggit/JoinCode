# 字节级清理 src/hosts 下 3 个 BOM 文件
$files = @(
    'd:\jcc-w1\src\hosts\JoinCode\ChatCommands\Bridge\PeersCommand.cs',
    'd:\jcc-w1\src\hosts\JoinCode\ChatCommands\Git\WorktreeCommand.cs',
    'd:\jcc-w1\src\hosts\JoinCode\ChatCommands\Tools\PluginCommand.cs'
)
foreach ($f in $files) {
    if (-not (Test-Path $f)) { Write-Host "MISSING: $f"; continue }
    $bytes = [System.IO.File]::ReadAllBytes($f)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $new = $bytes[3..($bytes.Length - 1)]
        [System.IO.File]::WriteAllBytes($f, $new)
        Write-Host "FIXED: $f"
    } else {
        Write-Host "NO BOM: $f"
    }
}
