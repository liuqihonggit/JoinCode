<#!/usr/bin/env pwsh
<#
.SYNOPSIS
    生成模块 README 的项目表，保证 README 与域内 csproj 一致。

.DESCRIPTION
    只替换 `<!-- BEGIN MODULE PROJECTS -->` 与 `<!-- END MODULE PROJECTS -->` 之间的内容。
    表格行数必须等于域内 csproj 数；域外构建依赖不写入本表。
    使用 `-Check` 只报告差异并以退出码 1 结束，供门禁/CI 调用。

.PARAMETER Domains
    仅处理指定域（逗号分隔），默认处理 modules 下全部域。

.PARAMETER Check
    只检查，不写文件；有漂移时退出码 1。

.PARAMETER RepoRoot
    仓库根目录，默认由脚本位置推导。
#>
[CmdletBinding()]
param(
    [string]$Domains = "",
    [switch]$Check,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
}

$beginMarker = "<!-- BEGIN MODULE PROJECTS -->"
$endMarker = "<!-- END MODULE PROJECTS -->"
$projectRowPattern = '^\|\s*[^|]+\|\s*[^|]+\|$'

function ConvertTo-RelativePortablePath {
    param([string]$Base, [string]$Path)
    return ([System.IO.Path]::GetRelativePath($Base, $Path) -replace '\\', '/')
}

function Get-ProjectRole {
    param([string]$RelativePath)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($RelativePath)
    if ($RelativePath -match '(^|/)sample/') { return "示例" }
    if ($RelativePath -match '(^|/)benchmarks?/') { return "基准测试" }
    if ($name -match '\.(tests?|e2e\.tests)$') { return "测试" }
    if ($name -match '\.tests\.') { return "测试" }
    if ($name -match '\.generator$' -or $name -match '\.Generator$') { return "源码生成器" }
    if ($name -match '^(NativeTui\.Host|NativeGui\.Host|JoinCode\.Engine)$') { return "应用入口" }
    return ""
}

function Read-OldDescriptions {
    param([string]$ReadmeText)
    $map = @{}
    foreach ($line in ($ReadmeText -split "`r?`n")) {
        if ($line -notmatch '^\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|$') { continue }
        $name = $Matches[1].Trim()
        $description = $Matches[2].Trim()
        if ($name -eq "项目" -or $name -match '^-+$') { continue }
        if ([string]::IsNullOrWhiteSpace($description)) { continue }
        $key = [System.IO.Path]::GetFileNameWithoutExtension($name)
        if (-not $map.ContainsKey($key)) { $map[$key] = $description }
    }
    return $map
}

function Get-SlnxProjects {
    param([string]$SlnxPath, [string]$BaseDirectory)
    if (-not (Test-Path -LiteralPath $SlnxPath)) { return @() }
    [xml]$xml = [System.IO.File]::ReadAllText($SlnxPath)
    $paths = @($xml.SelectNodes('//Project') | ForEach-Object { $_.GetAttribute("Path") })
    return @($paths | Where-Object { $_ } | ForEach-Object {
        [System.IO.Path]::GetFullPath((Join-Path $BaseDirectory $_))
    })
}

function Get-DomainProjectList {
    param([string]$DomainRoot)
    $excluded = @('obj', 'bin')
    $files = Get-ChildItem -LiteralPath $DomainRoot -Recurse -Filter *.csproj -File
    return @($files | Where-Object {
        $relative = ConvertTo-RelativePortablePath -Base $DomainRoot -Path $_.FullName
        ($relative -split '/')[0] -notin $excluded
    } | Sort-Object FullName)
}

function New-ProjectTable {
    param(
        [System.IO.FileInfo[]]$Projects,
        [string]$DomainRoot,
        [hashtable]$Descriptions
    )
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($beginMarker)
    $lines.Add("| 项目 | 说明 |")
    $lines.Add("|------|------|")
    foreach ($project in $Projects) {
        $relative = ConvertTo-RelativePortablePath -Base $DomainRoot -Path $project.FullName
        $display = [System.IO.Path]::GetFileNameWithoutExtension($relative)
        $key = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
        $description = if ($Descriptions.ContainsKey($key)) { $Descriptions[$key] } else { Get-ProjectRole $relative }
        $lines.Add("| [$display]($relative) | $description |")
    }
    $lines.Add($endMarker)
    return ($lines -join "`r`n")
}

function Update-Readme {
    param([string]$DomainRoot, [switch]$OnlyCheck)
    $readmePath = Join-Path $DomainRoot "README.md"
    if (-not (Test-Path -LiteralPath $readmePath)) {
        return [pscustomobject]@{ Domain = (Split-Path -Leaf $DomainRoot); Status = "missing-readme"; Changed = $false }
    }

    $projects = Get-DomainProjectList -DomainRoot $DomainRoot
    $text = [System.IO.File]::ReadAllText($readmePath)
    $descriptions = Read-OldDescriptions -ReadmeText $text
    $table = New-ProjectTable -Projects $projects -DomainRoot $DomainRoot -Descriptions $descriptions

    $start = $text.IndexOf($beginMarker, [System.StringComparison]::Ordinal)
    $end = $text.IndexOf($endMarker, [System.StringComparison]::Ordinal)
    $newText = if ($start -ge 0 -and $end -gt $start) {
        $prefix = $text.Substring(0, $start)
        $suffix = $text.Substring($end + $endMarker.Length)
        $prefix + $table + $suffix
    } else {
        $heading = "## 项目"
        $headingIndex = $text.IndexOf($heading, [System.StringComparison]::Ordinal)
        if ($headingIndex -lt 0) {
            throw "README 缺少 `## 项目` 段且无生成标记: $readmePath"
        }
        $sectionStart = $headingIndex + $heading.Length
        $nextSection = $text.IndexOf("`r`n## ", $sectionStart, [System.StringComparison]::Ordinal)
        if ($nextSection -lt 0) { $nextSection = $text.Length }
        $prefix = $text.Substring(0, $sectionStart)
        $suffix = $text.Substring($nextSection)
        $prefix + "`r`n`r`n" + $table + "`r`n" + $suffix
    }

    $changed = $newText -ne $text
    if ($changed -and -not $OnlyCheck) {
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($readmePath, $newText, $utf8NoBom)
    }

    return [pscustomobject]@{
        Domain   = (Split-Path -Leaf $DomainRoot)
        Status   = $(if ($changed) { "drift" } else { "ok" })
        Changed  = $changed
        Projects = $projects.Count
        Rows     = @($newText -split "`r?`n" | Where-Object { $_ -match $projectRowPattern -and $_ -notmatch '^\|\s*项目\s*\|' -and $_ -notmatch '^\|\s*-+' }).Count
    }
}

$roots = @()
if (-not [string]::IsNullOrWhiteSpace($Domains)) {
    foreach ($domain in ($Domains.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
        $roots += Join-Path (Join-Path $RepoRoot "modules") $domain
    }
} else {
    $roots = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot "modules") -Directory | ForEach-Object { $_.FullName })
}

$results = @()
foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $results += Update-Readme -DomainRoot $root -OnlyCheck:$Check
}

$drift = @($results | Where-Object { $_.Changed })
foreach ($result in $results) {
    $flag = if ($result.Changed) { "DRIFT" } else { "OK" }
    "{0,-20} {1,-8} projects={2}" -f $result.Domain, $flag, $result.Projects
}
if ($drift.Count -gt 0) {
    if ($Check) {
        Write-Error "模块 README 项目表漂移: $($drift.Count) 个域"
        exit 1
    }
    Write-Host "已更新 $($drift.Count) 个模块 README"
}
exit 0
