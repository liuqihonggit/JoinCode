[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [ValidateSet(
        'Guard','Brain','Hands','Llm','Agents','Reasoning',
        'Vault','Scheduling','McpToolDispatch','CodeIndex','Browser',
        'Mcp','Eyes','Dream','Bridge',
        'Composition','Clock',
        'Infra','Host',
        'AotSafety','JccAuditCli',
        'All'
    )]
    [string]$Project = 'Guard',

    [int]$Concurrency = 2,

    [int]$BreakThreshold = 0,

    [int]$LowThreshold = 60,

    [int]$HighThreshold = 80,

    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$projectMap = @{
    'Guard'         = @{ Dir = 'core\safety\Guard\tests\Unit';           Src = 'Guard.csproj' }
    'Vault'         = @{ Dir = 'core\safety\Vault\tests\Unit';           Src = 'Vault.csproj' }
    'Brain'         = @{ Dir = 'core\execution\Brain\tests\Unit';        Src = 'Brain.csproj' }
    'Hands'         = @{ Dir = 'core\execution\Hands\tests\Unit';        Src = 'Hands.csproj' }
    'Scheduling'    = @{ Dir = 'core\execution\Scheduling\tests\Unit';   Src = 'Scheduling.csproj' }
    'McpToolDispatch' = @{ Dir = 'core\execution\McpToolDispatch\tests\Unit'; Src = 'McpToolDispatch.csproj' }
    'Llm'           = @{ Dir = 'core\ai\Llm\tests\Unit';                Src = 'Llm.csproj' }
    'Agents'        = @{ Dir = 'core\ai\Agents\tests\Unit';             Src = 'Agents.csproj' }
    'Reasoning'     = @{ Dir = 'core\ai\Reasoning\tests\Unit';          Src = 'Reasoning.csproj' }
    'CodeIndex'     = @{ Dir = 'core\search\CodeIndex\tests\Unit';      Src = 'CodeIndex.csproj' }
    'Browser'       = @{ Dir = 'core\search\Browser\tests\Unit';        Src = 'Browser.csproj' }
    'Mcp'           = @{ Dir = 'services\Mcp\tests\Unit';               Src = 'Mcp.csproj' }
    'Eyes'          = @{ Dir = 'services\Eyes\tests\Unit';              Src = 'Eyes.csproj' }
    'Dream'         = @{ Dir = 'services\Dream\tests\Unit';             Src = 'Dream.csproj' }
    'Bridge'        = @{ Dir = 'services\Bridge\tests\Unit';            Src = 'Bridge.csproj' }
    'Composition'   = @{ Dir = 'composition\Composition\tests\Unit';    Src = 'Composition.csproj' }
    'Clock'         = @{ Dir = 'composition\Clock\tests\Unit';          Src = 'Clock.csproj' }
    'Infra'         = @{ Dir = 'tests\Unit\Infra.Tests';                Src = 'Infrastructure.csproj' }
    'Host'          = @{ Dir = 'tests\Unit\Host.Tests';                 Src = 'JoinCode.csproj' }
    'AotSafety'     = @{ Dir = 'generators\AotSafety.Generator\tests';  Src = 'AotSafety.Generator.csproj' }
    'JccAuditCli'   = @{ Dir = 'tools\JccAuditAstCli\tests';           Src = 'JccAuditCli.csproj' }
}

function Run-Stryker {
    param([string]$Name, [hashtable]$Info)

    $testDir = Join-Path $repoRoot $Info.Dir
    if (-not (Test-Path $testDir)) {
        Write-Warning "[$Name] Test directory not found: $testDir"
        return
    }

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  Stryker Mutation Testing: $Name" -ForegroundColor Cyan
    Write-Host "  Source: $($Info.Src)" -ForegroundColor Cyan
    Write-Host "  Directory: $testDir" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $args = @(
        '-p', $Info.Src,
        '-c', $Concurrency,
        '-b', $BreakThreshold,
        '--threshold-low', $LowThreshold,
        '--threshold-high', $HighThreshold,
        '-r', 'progress',
        '-r', 'html',
        '-r', 'clear-text'
    )

    if ($OpenReport) {
        $args += @('-o', 'html')
    }

    Push-Location $testDir
    try {
        dotnet-stryker @args
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            Write-Warning "[$Name] Stryker exited with code $exitCode (mutation score below threshold)"
        } else {
            Write-Host "[$Name] Stryker completed successfully" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
}

if ($Project -eq 'All') {
    foreach ($name in $projectMap.Keys | Sort-Object) {
        Run-Stryker -Name $name -Info $projectMap[$name]
    }
} else {
    $info = $projectMap[$Project]
    if (-not $info) {
        Write-Error "Unknown project: $Project. Valid options: $($projectMap.Keys -join ', '), All"
        exit 1
    }
    Run-Stryker -Name $Project -Info $info
}
