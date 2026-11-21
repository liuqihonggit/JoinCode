# 架构门禁（只读扫描 + 基线对比）。规则目录见 docs/proposed/design/DSG052。
# 设计约束：只读取仓库文件，不执行构建、不修改 csproj；基线写入仅在 -Mode Baseline 时发生。
param(
    [string]$RepoRoot = "",
    [ValidateSet("Report", "Baseline", "Validate")]
    [string]$Mode = "Report",
    [string]$BaselinePath = "",
    [string]$AllowlistPath = "",
    [switch]$IncludeTests,
    [string]$Domains = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
}
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "RepoRoot 不存在: $RepoRoot"
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\', '/')

if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot "baseline.json"
}
if ([string]::IsNullOrWhiteSpace($AllowlistPath)) {
    $AllowlistPath = Join-Path $PSScriptRoot "allowlist.json"
}

$script:ModuleDomains = @(
    "abstractions", "apps", "async_primitives", "collections", "cognition",
    "composition", "generators", "guard", "llm", "mcp", "perception",
    "persistence", "plugins", "services", "storage", "tools"
)
$script:UiDomains = @("native_tui", "native_gui")
$script:ContractDomains = @($script:ModuleDomains + $script:UiDomains)
$script:ExcludedPathPattern = '\\(bin|obj|artifacts|\.xxx|node_modules|\.git)\\'

function Get-RelativePath {
    param([string]$FullPath)
    return [System.IO.Path]::GetRelativePath($RepoRoot, $FullPath)
}

function ConvertTo-PortablePath {
    param([string]$Path)
    return ($Path -replace '\\', '/')
}

function Get-DomainOfPath {
    param([string]$FullPath)
    $rel = Get-RelativePath $FullPath
    $segments = $rel -split '[\\/]'
    if ($segments.Count -ge 2 -and $segments[0] -eq "modules") { return $segments[1] }
    if ($segments.Count -ge 1 -and $segments[0] -eq "native_tui") { return "native_tui" }
    if ($segments.Count -ge 1 -and $segments[0] -eq "native_gui") { return "native_gui" }
    if ($segments.Count -ge 1 -and $segments[0] -eq "test") { return "test" }
    return "other"
}

function Test-IsTestProject {
    param([string]$FullPath)
    $rel = Get-RelativePath $FullPath
    $segments = @($rel -split '[\\/]')
    foreach ($segment in $segments) {
        if ($segment -eq "test" -or $segment -eq "tests") { return $true }
        if ($segment -match '\.(tests?)$') { return $true }
    }
    if ($FullPath -match '\.(tests?|Tests?)\.csproj$') { return $true }
    return $false
}

function Resolve-ReferencePath {
    param([string]$Raw, [string]$FromDirectory)
    $value = $Raw
    $value = $value.Replace('$(RepoRoot)', $RepoRoot.TrimEnd('\') + '\')
    $value = $value.Replace('$(MSBuildThisFileDirectory)', $FromDirectory.TrimEnd('\') + '\')
    $value = $value.Replace('$(MSBuildProjectDirectory)', $FromDirectory.TrimEnd('\') + '\')
    $value = $value -replace '/', '\'
    if ([System.IO.Path]::IsPathRooted($value)) {
        return [System.IO.Path]::GetFullPath($value)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $FromDirectory $value))
}

function Get-CsprojFiles {
    return @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.csproj -File |
        Where-Object { $_.FullName -notmatch $script:ExcludedPathPattern })
}

function Read-ArchitectureAllowlist {
    param([string]$Path)

    $problems = [System.Collections.Generic.List[string]]::new()
    $entries = [System.Collections.Generic.List[object]]::new()

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Exists = $false; Path = $Path; Entries = $entries; Problems = $problems }
    }

    try {
        $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        $problems.Add("allowlist JSON 无法解析: $($_.Exception.Message)")
        return [pscustomobject]@{ Exists = $true; Path = $Path; Entries = $entries; Problems = $problems }
    }

    if ($null -eq $document.entries) {
        $problems.Add("allowlist 缺少 entries 数组")
        return [pscustomobject]@{ Exists = $true; Path = $Path; Entries = $entries; Problems = $problems }
    }

    $seen = @{}
    foreach ($raw in @($document.entries)) {
        if ($null -eq $raw) { continue }
        $from = ([string]$raw.from).Trim()
        $to = ([string]$raw.to).Trim()
        $reason = ([string]$raw.reason).Trim()
        $adr = ([string]$raw.adr).Trim()
        $expiresText = ([string]$raw.expires).Trim()
        $isValid = $true
        $isExpired = $false
        $expires = $null

        if ($from.Length -eq 0 -or $to.Length -eq 0) {
            $problems.Add("allowlist 条目缺少 from/to: $from -> $to")
            $isValid = $false
        } elseif (-not ($script:ContractDomains -contains $from) -or -not ($script:ContractDomains -contains $to)) {
            $problems.Add("allowlist 条目含未知域: $from -> $to")
            $isValid = $false
        } elseif ($from -eq $to) {
            $problems.Add("allowlist 条目 from/to 相同: $from")
            $isValid = $false
        }

        if ($reason.Length -eq 0) {
            $problems.Add("allowlist 条目缺少 reason: $from -> $to")
            $isValid = $false
        }
        if ($adr.Length -eq 0) {
            $problems.Add("allowlist 条目缺少 adr: $from -> $to")
            $isValid = $false
        }

        if ($expiresText.Length -eq 0) {
            $problems.Add("allowlist 条目缺少 expires: $from -> $to")
            $isValid = $false
        } else {
            try {
                $expires = [datetime]::ParseExact($expiresText, "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
                if ($expires.Date -lt (Get-Date).Date) {
                    $isExpired = $true
                    $problems.Add("allowlist 条目已过期: $from -> $to (expires $expiresText)")
                }
            } catch {
                $problems.Add("allowlist expires 非法（要求 yyyy-MM-dd）: $from -> $to = $expiresText")
                $isValid = $false
            }
        }

        $key = "$from->$to"
        if ($seen.ContainsKey($key)) {
            $problems.Add("allowlist 条目重复: $key")
            $isValid = $false
        } elseif ($from.Length -gt 0 -and $to.Length -gt 0) {
            $seen[$key] = $true
        }

        $entries.Add([pscustomobject]@{
            From      = $from
            To        = $to
            Reason    = $reason
            Adr       = $adr
            Expires   = $expires
            ExpiresText = $expiresText
            IsValid   = $isValid
            IsExpired = $isExpired
        })
    }

    return [pscustomobject]@{ Exists = $true; Path = $Path; Entries = $entries; Problems = $problems }
}

function Read-CsprojXml {
    param([string]$Path)
    return [xml](Get-Content -LiteralPath $Path -Raw)
}

function Test-IsAnalyzerReference {
    param($Node)
    $outputItemType = $Node.GetAttribute("OutputItemType")
    if ([string]::IsNullOrWhiteSpace($outputItemType)) {
        $child = $Node.SelectSingleNode('*[local-name()="OutputItemType"]')
        if ($null -ne $child) { $outputItemType = $child.InnerText.Trim() }
    }
    return ($outputItemType -eq "Analyzer")
}

function Get-DomainCycles {
    param($Edges)
    $graph = @{}
    foreach ($edge in $Edges) {
        if ($edge.FromDomain -eq $edge.ToDomain) { continue }
        if (-not $graph.ContainsKey($edge.FromDomain)) {
            $graph[$edge.FromDomain] = [System.Collections.Generic.List[string]]::new()
        }
        if (-not $graph[$edge.FromDomain].Contains($edge.ToDomain)) {
            $graph[$edge.FromDomain].Add($edge.ToDomain)
        }
    }

    $script:cycleGraph = $graph
    $script:cycleList = [System.Collections.Generic.List[string]]::new()
    $script:cycleSeen = @{}
    $script:cycleStack = @{}
    $script:cycleVisited = @{}

    function Get-CanonicalCycleKey {
        param([string[]]$Nodes)
        if ($Nodes.Count -eq 0) { return "" }
        $min = 0
        for ($i = 1; $i -lt $Nodes.Count; $i++) {
            if ($Nodes[$i] -lt $Nodes[$min]) { $min = $i }
        }
        $rotated = @()
        for ($i = 0; $i -lt $Nodes.Count; $i++) {
            $rotated += $Nodes[($min + $i) % $Nodes.Count]
        }
        return (($rotated + $rotated[0]) -join " -> ")
    }

    function Invoke-CycleWalk {
        param([string]$Node, [string[]]$Path)
        if ($script:cycleStack.ContainsKey($Node)) {
            $index = [array]::IndexOf($Path, $Node)
            if ($index -ge 0) {
                $nodes = @($Path[$index..($Path.Count - 1)])
                $key = Get-CanonicalCycleKey $nodes
                if (-not [string]::IsNullOrWhiteSpace($key) -and -not $script:cycleSeen.ContainsKey($key)) {
                    $script:cycleSeen[$key] = $true
                    $script:cycleList.Add($key)
                }
            }
            return
        }
        if ($script:cycleVisited.ContainsKey($Node)) { return }
        $script:cycleVisited[$Node] = $true
        $script:cycleStack[$Node] = $true
        if ($script:cycleGraph.ContainsKey($Node)) {
            foreach ($next in @($script:cycleGraph[$Node] | Sort-Object -Unique)) {
                Invoke-CycleWalk -Node $next -Path (@($Path) + $Node)
            }
        }
        $script:cycleStack.Remove($Node)
    }

    foreach ($domain in @($graph.Keys | Sort-Object)) {
        Invoke-CycleWalk -Node $domain -Path @()
    }
    return @($script:cycleList | Sort-Object)
}

function Get-SlnxProjects {
    param([string]$SlnxPath, [string]$BaseDirectory)
    if (-not (Test-Path -LiteralPath $SlnxPath)) { return @() }
    $xml = Read-CsprojXml $SlnxPath
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($node in $xml.SelectNodes('//*[local-name()="Project"]')) {
        $path = $node.GetAttribute("Path")
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $resolved = Resolve-ReferencePath -Raw $path -FromDirectory $BaseDirectory
        if (Test-Path -LiteralPath $resolved) { $result.Add($resolved) }
    }
    return @($result | Sort-Object -Unique)
}

function Get-DomainContractState {
    param([string]$Domain)
    $propsPath = if ($Domain -eq "native_tui" -or $Domain -eq "native_gui") {
        Join-Path (Join-Path $RepoRoot $Domain) "Directory.Build.props"
    } else {
        Join-Path (Join-Path (Join-Path $RepoRoot "modules") $Domain) "Directory.Build.props"
    }
    $hasFile = Test-Path -LiteralPath $propsPath
    $declaredDomain = ""
    $hasNamespaceRoots = $false

    if ($hasFile) {
        $xml = Read-CsprojXml $propsPath
        $domainNode = $xml.SelectSingleNode('//*[local-name()="JccDomain"]')
        if ($null -ne $domainNode) { $declaredDomain = $domainNode.InnerText.Trim() }
        $namespaceNode = $xml.SelectSingleNode('//*[local-name()="JccNamespaceRoots"]')
        $hasNamespaceRoots = ($null -ne $namespaceNode -and -not [string]::IsNullOrWhiteSpace($namespaceNode.InnerText))
    }

    return [pscustomobject]@{
        Domain          = $Domain
        PropsPath       = Get-RelativePath $propsPath
        HasProps        = $hasFile
        DeclaredDomain  = $declaredDomain
        HasNamespaceRoots = $hasNamespaceRoots
        IsValid         = ($hasFile -and $declaredDomain -eq $Domain)
    }
}

function Get-ReadmeProjectRowCount {
    param([string]$ReadmePath)
    if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) { return -1 }
    $text = [System.IO.File]::ReadAllText($ReadmePath)
    $begin = "<!-- BEGIN MODULE PROJECTS -->"
    $end = "<!-- END MODULE PROJECTS -->"
    $startIndex = $text.IndexOf($begin, [System.StringComparison]::Ordinal)
    $endIndex = $text.IndexOf($end, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) { return -1 }
    $block = $text.Substring($startIndex, $endIndex - $startIndex)
    return @($block -split "`r?`n" | Where-Object { $_ -match '^\|\s*[^|]+\|\s*[^|]+\|$' -and $_ -notmatch '^\|\s*项目\s*\|' -and $_ -notmatch '^\|\s*-+' }).Count
}

function Get-DomainDocState {
    param([string]$Domain, [int]$ExpectedProjects = 0)
    $domainRoot = if ($Domain -eq "native_tui" -or $Domain -eq "native_gui") {
        Join-Path $RepoRoot $Domain
    } else {
        Join-Path (Join-Path $RepoRoot "modules") $Domain
    }
    $required = @(
        @{ Name = "README.md"; Path = (Join-Path $domainRoot "README.md"); Kind = "file" },
        @{ Name = "CHANGELOG.md"; Path = (Join-Path $domainRoot "CHANGELOG.md"); Kind = "file" },
        @{ Name = "docs/"; Path = (Join-Path $domainRoot "docs"); Kind = "directory" }
    )
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $required) {
        $exists = if ($item.Kind -eq "directory") {
            Test-Path -LiteralPath $item.Path -PathType Container
        } else {
            Test-Path -LiteralPath $item.Path -PathType Leaf
        }
        if (-not $exists) { $missing.Add($item.Name) }
    }
    $readmePath = Join-Path $domainRoot "README.md"
    $readmeRows = Get-ReadmeProjectRowCount $readmePath
    if ($ExpectedProjects -gt 0 -and $readmeRows -ne $ExpectedProjects) {
        $displayRows = if ($readmeRows -lt 0) { "未生成" } else { "$readmeRows" }
        $missing.Add("README 项目表 $displayRows/$ExpectedProjects")
    }
    return [pscustomobject]@{
        Domain      = $Domain
        DomainRoot  = ConvertTo-PortablePath (Get-RelativePath $domainRoot)
        Missing     = @($missing)
        IsComplete  = ($missing.Count -eq 0)
        ReadmeRows  = $readmeRows
        ExpectedProjects = $ExpectedProjects
    }
}

function Get-DomainPropsApiVisibility {
    param([string]$Domain)
    $propsPath = if ($Domain -eq "native_tui" -or $Domain -eq "native_gui") {
        Join-Path (Join-Path $RepoRoot $Domain) "Directory.Build.props"
    } else {
        Join-Path (Join-Path (Join-Path $RepoRoot "modules") $Domain) "Directory.Build.props"
    }
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) { return $null }
    try {
        $xml = Read-CsprojXml $propsPath
    } catch {
        return $null
    }
    $node = $xml.SelectSingleNode('//*[local-name()="JccApiVisibility"]')
    if ($null -eq $node) { return $null }
    $value = $node.InnerText.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

# 项目级覆盖优先，其次域契约默认，都没有按 internal（未准入）
$script:apiVisibilityCache = @{}
function Get-ProjectApiVisibility {
    param([string]$ProjectPath, [string]$Domain)
    if ($script:apiVisibilityCache.ContainsKey($ProjectPath)) {
        return $script:apiVisibilityCache[$ProjectPath]
    }
    $value = $null
    try {
        $xml = Read-CsprojXml $ProjectPath
        $node = $xml.SelectSingleNode('//*[local-name()="JccApiVisibility"]')
        if ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            $value = $node.InnerText.Trim()
        }
    } catch {
        $value = $null
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = Get-DomainPropsApiVisibility $Domain
    }
    if ([string]::IsNullOrWhiteSpace($value)) { $value = "internal" }
    $script:apiVisibilityCache[$ProjectPath] = $value
    return $value
}

# ---------- 扫描 ----------
$projectFiles = Get-CsprojFiles
$projects = @{}
$edges = [System.Collections.Generic.List[object]]::new()

foreach ($file in $projectFiles) {
    $domain = Get-DomainOfPath $file.FullName
    $isTest = Test-IsTestProject $file.FullName
    $refs = [System.Collections.Generic.List[object]]::new()

    try {
        $xml = Read-CsprojXml $file.FullName
    } catch {
        Write-Warning "跳过无法解析的 csproj: $(Get-RelativePath $file.FullName)"
        continue
    }

    foreach ($node in $xml.SelectNodes('//*[local-name()="ProjectReference"]')) {
        $include = $node.GetAttribute("Include")
        if ([string]::IsNullOrWhiteSpace($include)) { continue }
        $target = Resolve-ReferencePath -Raw $include -FromDirectory $file.DirectoryName
        if (-not (Test-Path -LiteralPath $target)) { continue }

        $kind = if (Test-IsAnalyzerReference $node) { "analyzer" } else { "code" }
        $refs.Add([pscustomobject]@{ Target = $target; Kind = $kind })
        $edges.Add([pscustomobject]@{
            From       = $file.FullName
            To         = $target
            FromDomain = $domain
            ToDomain   = Get-DomainOfPath $target
            Kind       = $kind
            FromTest   = $isTest
            ToTest     = Test-IsTestProject $target
        })
    }

    $projects[$file.FullName] = [pscustomobject]@{
        Path   = $file.FullName
        Rel    = Get-RelativePath $file.FullName
        Domain = $domain
        IsTest = $isTest
        Refs   = $refs
    }
}

$codeEdges = @($edges | Where-Object { $_.Kind -eq "code" })
$prodEdges = if ($IncludeTests) {
    @($codeEdges)
} else {
    @($codeEdges | Where-Object { -not $_.FromTest -and -not $_.ToTest })
}
$prodCrossEdges = @($prodEdges | Where-Object { $_.FromDomain -ne $_.ToDomain })
$prodToTestEdges = @($codeEdges | Where-Object { -not $_.FromTest -and $_.ToTest })

if (-not [string]::IsNullOrWhiteSpace($Domains)) {
    $domainFilter = @($Domains.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $prodEdges = @($prodEdges | Where-Object { $domainFilter -contains $_.FromDomain })
    $prodCrossEdges = @($prodEdges | Where-Object { $_.FromDomain -ne $_.ToDomain })
}

$apiProjects = @($prodCrossEdges | ForEach-Object { ConvertTo-PortablePath (Get-RelativePath $_.To) } | Sort-Object -Unique)
# JCC12003：跨域 code 边目标必须是显式 public 的项目（项目级覆盖 > 域契约默认 > internal）
$apiVisibilityViolations = @($prodCrossEdges | Where-Object {
    (Get-ProjectApiVisibility -ProjectPath $_.To -Domain $_.ToDomain) -ne "public"
})
$contractStates = @($script:ContractDomains | ForEach-Object { Get-DomainContractState $_ })
$invalidContracts = @($contractStates | Where-Object { -not $_.IsValid })
$docStates = @($script:ModuleDomains | ForEach-Object {
    $domain = $_
    $expectedProjects = @($projects.Values | Where-Object { $_.Domain -eq $domain }).Count
    Get-DomainDocState -Domain $domain -ExpectedProjects $expectedProjects
})
$incompleteDocs = @($docStates | Where-Object { -not $_.IsComplete })

$slnxMissing = [System.Collections.Generic.List[object]]::new()
$slnxExternal = [System.Collections.Generic.List[object]]::new()
foreach ($domain in $script:ModuleDomains) {
    $domainRoot = Join-Path (Join-Path $RepoRoot "modules") $domain
    $slnxPath = Join-Path $domainRoot "module.slnx"
    $domainProjects = @($projects.Values | Where-Object { $_.Domain -eq $domain })
    $slnxProjects = @(Get-SlnxProjects -SlnxPath $slnxPath -BaseDirectory $domainRoot)

    if (-not (Test-Path -LiteralPath $slnxPath)) {
        $slnxMissing.Add([pscustomobject]@{ Domain = $domain; Project = "<module.slnx missing>"; Kind = "slnx" })
        continue
    }

    foreach ($project in $domainProjects) {
        if ($slnxProjects -notcontains $project.Path) {
            $slnxMissing.Add([pscustomobject]@{ Domain = $domain; Project = ConvertTo-PortablePath $project.Rel; Kind = "missing" })
        }
    }
    foreach ($target in $slnxProjects) {
        if ((Get-DomainOfPath $target) -ne $domain) {
            $slnxExternal.Add([pscustomobject]@{ Domain = $domain; Project = ConvertTo-PortablePath (Get-RelativePath $target); Kind = "external" })
        }
    }
}

$directoryViolations = @(
    Get-ChildItem -LiteralPath (Join-Path $RepoRoot "modules") -Directory -Recurse -Force -ErrorAction Stop |
        Where-Object { $_.FullName -notmatch $script:ExcludedPathPattern } |
        Where-Object { $_.Name -cnotmatch '^[a-z0-9_]+$' }
)

$ivtProjects = [System.Collections.Generic.List[object]]::new()
foreach ($project in $projects.Values) {
    try { $xml = Read-CsprojXml $project.Path } catch { continue }
    $nodes = @($xml.SelectNodes('//*[local-name()="InternalsVisibleTo"]'))
    if ($nodes.Count -gt 0) {
        $ivtProjects.Add([pscustomobject]@{ Project = ConvertTo-PortablePath $project.Rel; Count = $nodes.Count })
    }
}

$allowlist = Read-ArchitectureAllowlist -Path $AllowlistPath
$allowEntries = @($allowlist.Entries)
$allowProblems = @($allowlist.Problems)
$activeAllowPairs = @(
    $allowEntries |
        Where-Object { $_.IsValid -and -not $_.IsExpired } |
        ForEach-Object { "$($_.From)->$($_.To)" }
)

$cycles = @(Get-DomainCycles -Edges $prodEdges)
$inDegree = @{}
foreach ($edge in $prodEdges) {
    $key = $edge.To
    if (-not $inDegree.ContainsKey($key)) { $inDegree[$key] = 0 }
    $inDegree[$key] = $inDegree[$key] + 1
}
$hubTop = @($inDegree.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 |
    ForEach-Object { [pscustomobject]@{ Project = ConvertTo-PortablePath (Get-RelativePath $_.Key); InDegree = $_.Value } })

$ruleCounts = [ordered]@{
    "JCC12001" = $invalidContracts.Count
    "JCC12002" = $prodCrossEdges.Count
    "JCC12003" = $apiVisibilityViolations.Count
    "JCC12004" = $prodToTestEdges.Count
    "JCC12005" = $cycles.Count
    "JCC12009" = $directoryViolations.Count
    "JCC12010" = $slnxMissing.Count
    "JCC12011" = $incompleteDocs.Count
    "JCC12012" = 0
}
$ruleCountsByDomain = [ordered]@{
    "JCC12002" = [ordered]@{}
    "JCC12003" = [ordered]@{}
    "JCC12004" = [ordered]@{}
    "JCC12005" = [ordered]@{}
    "JCC12009" = [ordered]@{}
    "JCC12010" = [ordered]@{}
    "JCC12011" = [ordered]@{}
}
foreach ($edge in $prodCrossEdges) {
    if (-not $ruleCountsByDomain["JCC12002"].Contains($edge.FromDomain)) {
        $ruleCountsByDomain["JCC12002"][$edge.FromDomain] = 0
    }
    $ruleCountsByDomain["JCC12002"][$edge.FromDomain] = $ruleCountsByDomain["JCC12002"][$edge.FromDomain] + 1
}
foreach ($edge in $apiVisibilityViolations) {
    if (-not $ruleCountsByDomain["JCC12003"].Contains($edge.ToDomain)) {
        $ruleCountsByDomain["JCC12003"][$edge.ToDomain] = 0
    }
    $ruleCountsByDomain["JCC12003"][$edge.ToDomain] = $ruleCountsByDomain["JCC12003"][$edge.ToDomain] + 1
}
foreach ($edge in $prodToTestEdges) {
    if (-not $ruleCountsByDomain["JCC12004"].Contains($edge.FromDomain)) {
        $ruleCountsByDomain["JCC12004"][$edge.FromDomain] = 0
    }
    $ruleCountsByDomain["JCC12004"][$edge.FromDomain] = $ruleCountsByDomain["JCC12004"][$edge.FromDomain] + 1
}
foreach ($item in $slnxMissing) {
    if (-not $ruleCountsByDomain["JCC12010"].Contains($item.Domain)) {
        $ruleCountsByDomain["JCC12010"][$item.Domain] = 0
    }
    $ruleCountsByDomain["JCC12010"][$item.Domain] = $ruleCountsByDomain["JCC12010"][$item.Domain] + 1
}
foreach ($item in $directoryViolations) {
    $domain = Get-DomainOfPath $item.FullName
    if (-not $ruleCountsByDomain["JCC12009"].Contains($domain)) {
        $ruleCountsByDomain["JCC12009"][$domain] = 0
    }
    $ruleCountsByDomain["JCC12009"][$domain] = $ruleCountsByDomain["JCC12009"][$domain] + 1
}
foreach ($item in $incompleteDocs) {
    $ruleCountsByDomain["JCC12011"][$item.Domain] = 1
}
if ($cycles.Count -gt 0) { $ruleCountsByDomain["JCC12005"]["*"] = $cycles.Count }

$snapshot = [ordered]@{
    version         = 1
    generated       = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss+08:00")
    repoRoot        = "."
    projectCount    = $projects.Count
    edgeCount       = $edges.Count
    codeEdgeCount   = $codeEdges.Count
    prodEdgeCount   = $prodEdges.Count
    prodCrossEdgeCount = $prodCrossEdges.Count
    contracts       = [ordered]@{
        expected = $script:ContractDomains.Count
        valid    = ($contractStates.Count - $invalidContracts.Count)
        states   = @($contractStates | ForEach-Object {
            [ordered]@{
                Domain            = $_.Domain
                PropsPath         = ConvertTo-PortablePath $_.PropsPath
                HasProps          = $_.HasProps
                DeclaredDomain    = $_.DeclaredDomain
                HasNamespaceRoots = $_.HasNamespaceRoots
                IsValid           = $_.IsValid
            }
        })
    }
    docs            = [ordered]@{
        expected  = $script:ModuleDomains.Count
        complete  = ($docStates.Count - $incompleteDocs.Count)
        missing   = @($incompleteDocs | ForEach-Object { [ordered]@{ Domain = $_.Domain; Missing = $_.Missing } })
    }
    ruleCounts      = $ruleCounts
    ruleCountsByDomain = $ruleCountsByDomain
    crossDomainEdges = @($prodCrossEdges |
        ForEach-Object { "$($_.FromDomain)->$($_.ToDomain)" } |
        Sort-Object -Unique)
    cycles          = $cycles
    hubBudget       = @($hubTop | ForEach-Object {
        [ordered]@{ Project = ConvertTo-PortablePath $_.Project; InDegree = $_.InDegree }
    })
    apiProjects     = @($apiProjects | ForEach-Object { ConvertTo-PortablePath $_ })
    slnxMissing     = @($slnxMissing | ForEach-Object { ConvertTo-PortablePath $_.Project })
    slnxExternal    = @($slnxExternal | ForEach-Object { "$($_.Domain):$(ConvertTo-PortablePath $_.Project)" })
    directoryViolations = @($directoryViolations | ForEach-Object { ConvertTo-PortablePath (Get-RelativePath $_.FullName) })
    ivtProjects     = @($ivtProjects | ForEach-Object { ConvertTo-PortablePath $_.Project })
    allow           = @($allowEntries | ForEach-Object {
        [ordered]@{
            From      = $_.From
            To        = $_.To
            Reason    = $_.Reason
            Adr       = $_.Adr
            Expires   = $_.ExpiresText
            IsValid   = $_.IsValid
            IsExpired = $_.IsExpired
        }
    })
}

# ---------- 报告 ----------
$report = [System.Text.StringBuilder]::new()
function Add-ReportLine { param([string]$Text) [void]$report.AppendLine($Text) }

Add-ReportLine "# 架构门禁报告 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-ReportLine "仓库: $RepoRoot"
Add-ReportLine "模式: $Mode"
Add-ReportLine "扫描: csproj=$($projects.Count) 边=$($edges.Count) code=$($codeEdges.Count) 生产=$($prodEdges.Count) 生产跨域=$($prodCrossEdges.Count)"
Add-ReportLine ""
Add-ReportLine "## 契约（JCC12001）"
Add-ReportLine "  有效域契约: $($snapshot.contracts.valid)/$($snapshot.contracts.expected)"
foreach ($state in $contractStates) {
    $mark = if ($state.IsValid) { "OK" } else { "MISSING" }
    Add-ReportLine ("  {0,-18} {1,-8} domain={2}" -f $state.Domain, $mark, $state.DeclaredDomain)
}
Add-ReportLine ""
Add-ReportLine "## 模块文档（JCC12011）"
Add-ReportLine "  齐备域: $($snapshot.docs.complete)/$($snapshot.docs.expected)"
foreach ($state in $incompleteDocs) {
    Add-ReportLine ("  {0,-18} 缺: {1}" -f $state.Domain, ($state.Missing -join ", "))
}
Add-ReportLine ""
Add-ReportLine "## allowlist（JCC12002）"
if (-not $allowlist.Exists) {
    Add-ReportLine "  文件不存在: $(ConvertTo-PortablePath (Get-RelativePath $AllowlistPath))"
} else {
    Add-ReportLine ("  文件: {0} 条目: {1}（生效 {2}）" -f (ConvertTo-PortablePath (Get-RelativePath $AllowlistPath)), $allowEntries.Count, $activeAllowPairs.Count)
    foreach ($problem in $allowProblems) { Add-ReportLine ("  PROBLEM " + $problem) }
}
Add-ReportLine ""
Add-ReportLine "## 规则计数"
foreach ($key in $ruleCounts.Keys) { Add-ReportLine ("  {0} = {1}" -f $key, $ruleCounts[$key]) }
Add-ReportLine ""
Add-ReportLine "## 生产域环（JCC12005，$($cycles.Count) 条）"
if ($cycles.Count -eq 0) { Add-ReportLine "  无" } else { foreach ($cycle in $cycles) { Add-ReportLine "  $cycle" } }
Add-ReportLine ""
Add-ReportLine "## 跨域目标 API 准入（JCC12003，$($apiVisibilityViolations.Count) 条）"
if ($apiVisibilityViolations.Count -eq 0) {
    Add-ReportLine "  无"
} else {
    foreach ($edge in ($apiVisibilityViolations | Select-Object -First 30)) {
        Add-ReportLine ("  [{0}->{1}] {2}" -f $edge.FromDomain, $edge.ToDomain, (ConvertTo-PortablePath (Get-RelativePath $edge.To)))
    }
    if ($apiVisibilityViolations.Count -gt 30) { Add-ReportLine "  ... 其余 $($apiVisibilityViolations.Count - 30) 条见基线" }
}
Add-ReportLine ""
Add-ReportLine "## code 入度 TOP 15（Hub 预算）"
foreach ($hub in ($hubTop | Select-Object -First 15)) { Add-ReportLine ("  {0,-6} {1}" -f $hub.InDegree, $hub.Project) }
Add-ReportLine ""
Add-ReportLine "## module.slnx 缺口（JCC12010）"
foreach ($item in $slnxMissing) { Add-ReportLine ("  [{0}] {1}" -f $item.Domain, $item.Project) }
Add-ReportLine ""
Add-ReportLine "## 目录命名违规（JCC12009，$($directoryViolations.Count) 个）"
foreach ($dir in ($directoryViolations | Select-Object -First 30)) { Add-ReportLine ("  " + (Get-RelativePath $dir.FullName)) }
if ($directoryViolations.Count -gt 30) { Add-ReportLine "  ... 其余 $($directoryViolations.Count - 30) 个见基线" }

$reportText = $report.ToString()
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportText | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Host "written: $OutputPath"
} else {
    Write-Host $reportText
}

if ($Mode -eq "Baseline") {
    $json = $snapshot | ConvertTo-Json -Depth 12
    $json | Set-Content -LiteralPath $BaselinePath -Encoding utf8
    Write-Host "baseline written: $BaselinePath"
    exit 0
}

if ($Mode -eq "Validate") {
    if (-not (Test-Path -LiteralPath $BaselinePath)) {
        Write-Host "baseline 不存在，先运行 -Mode Baseline: $BaselinePath" -ForegroundColor Red
        exit 1
    }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $failures = [System.Collections.Generic.List[string]]::new()

    if (-not $allowlist.Exists) {
        $failures.Add("allowlist 文件不存在: $(ConvertTo-PortablePath (Get-RelativePath $AllowlistPath))")
    }
    foreach ($problem in $allowProblems) {
        $failures.Add("allowlist 校验失败: $problem")
    }

    $baselineEdgeProperty = $baseline.PSObject.Properties['crossDomainEdges']
    $allowedNewPairEdges = 0
    if ($null -eq $baselineEdgeProperty) {
        Write-Host "baseline 缺少 crossDomainEdges，仅按计数比较；请运行 -Mode Baseline 重新冻结" -ForegroundColor Yellow
    } else {
        $baselinePairs = @($baselineEdgeProperty.Value)
        $allowedNewPairs = @($activeAllowPairs | Where-Object { $baselinePairs -notcontains $_ })
        $allowedNewPairEdges = @($prodCrossEdges | Where-Object {
            $pair = "$($_.FromDomain)->$($_.ToDomain)"
            $allowedNewPairs -contains $pair
        }).Count

        foreach ($pair in @($snapshot.crossDomainEdges)) {
            if ($baselinePairs -contains $pair) { continue }
            if ($activeAllowPairs -contains $pair) { continue }
            $failures.Add("JCC12002 新增跨域依赖未授权: $pair")
        }
    }

    foreach ($rule in $ruleCounts.Keys) {
        $current = $ruleCounts[$rule]
        if ($rule -eq "JCC12002" -and $null -ne $baselineEdgeProperty) {
            $current = [Math]::Max(0, $current - $allowedNewPairEdges)
        }
        $allowed = if ($null -ne $baseline.ruleCounts.$rule) { [int]$baseline.ruleCounts.$rule } else { 0 }
        if ($current -gt $allowed) {
            $failures.Add("$rule 增长: $allowed -> $current")
        } elseif ($current -lt $allowed) {
            Write-Host "$rule 下降: $allowed -> $current（可更新基线）" -ForegroundColor Yellow
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) { Write-Host "FAIL $failure" -ForegroundColor Red }
        exit 1
    }
    Write-Host "architecture gate: PASS" -ForegroundColor Green
    exit 0
}

exit 0
