param(
    [switch]$DryRun
)

$rootDir = "D:\DeepSeekTUI"
$excludeDirs = @('\artifacts\', '\.git\', '\.x\', '\obj\', '\bin\', '\.reference\')

$extensions = @('*.cs', '*.csproj', '*.props', '*.targets', '*.ps1')

$files = Get-ChildItem -Path $rootDir -Recurse -Include $extensions -File | Where-Object {
    $path = $_.FullName
    foreach ($ex in $excludeDirs) {
        if ($path -like "*$ex*") { return $false }
    }
    return $true
}

$totalMatches = 0
$totalFiles = 0

foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    if ($content.Contains('JoinCode')) {
        $totalFiles++
        $matchCount = ([regex]::Matches($content, 'JoinCode')).Count
        $totalMatches += $matchCount
        if ($DryRun) {
            Write-Output "$($f.FullName): $matchCount matches"
        } else {
            $newContent = $content.Replace('JoinCode', 'JoinCode')
            [System.IO.File]::WriteAllText($f.FullName, $newContent, (New-Object System.Text.UTF8Encoding $false))
        }
    }
}

if ($DryRun) {
    Write-Output ""
    Write-Output "Total: $totalFiles files, $totalMatches matches"
} else {
    Write-Output "Replaced in $totalFiles files ($totalMatches occurrences)"
}
