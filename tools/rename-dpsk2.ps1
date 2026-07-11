param(
    [switch]$DryRun
)

$rootDir = "D:\DeepSeekTUI"
$excludeDirs = @('\artifacts\', '\.git\', '\.x\', '\obj\', '\bin\', '\.reference\')
$extensions = @('*.cs', '*.csproj', '*.props', '*.targets', '*.ps1', '*.slnx')

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
    $hasChange = $false
    
    if ($content.Contains('JCC') -or $content.Contains('Jcc') -or $content.Contains('jcc')) {
        $newContent = $content
        $newContent = $newContent.Replace('JCC', 'JCC')
        $newContent = $newContent.Replace('Jcc', 'Jcc')
        $newContent = $newContent.Replace('jcc', 'jcc')
        
        $matchCount = 0
        $tmp = $content
        while ($tmp.Contains('JCC')) { $tmp = $tmp.Remove($tmp.IndexOf('JCC'), 4).Insert($tmp.IndexOf('JCC'), 'X'); $matchCount++ }
        $tmp = $content
        while ($tmp.Contains('Jcc')) { $tmp = $tmp.Remove($tmp.IndexOf('Jcc'), 4).Insert($tmp.IndexOf('Jcc'), 'X'); $matchCount++ }
        $tmp = $content
        while ($tmp.Contains('jcc')) { $tmp = $tmp.Remove($tmp.IndexOf('jcc'), 4).Insert($tmp.IndexOf('jcc'), 'X'); $matchCount++ }
        
        $totalFiles++
        $totalMatches += $matchCount
        
        if ($DryRun) {
            Write-Output "$($f.FullName): $matchCount matches"
        } else {
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
