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

$found = 0
foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    if ($content.Contains('JccSharpTUI')) {
        Write-Output $f.FullName
        $found++
    }
}

Write-Output ""
Write-Output "Remaining files with JccSharpTUI: $found"
