# 清理 Generator 项目的 obj 目录中的 DLL
$dlls = @(
    'D:\jcc-w1\generators\EnumMetadata.Generator\src\obj\Release\netstandard2.0\EnumMetadata.Generator.dll',
    'D:\jcc-w1\generators\AotSafety.Generator\src\obj\Release\netstandard2.0\AotSafety.Generator.dll'
)
foreach ($d in $dlls) {
    if (Test-Path $d) {
        try {
            Remove-Item -Path $d -Force -ErrorAction Stop
            Write-Host "REMOVED: $d"
        } catch {
            Write-Host "FAILED: $d - $($_.Exception.Message)"
        }
    }
}
