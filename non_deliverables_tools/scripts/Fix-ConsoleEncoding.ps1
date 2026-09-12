<#
.SYNOPSIS
    控制台编码检测与修复脚本 - 一键解决中文乱码
.DESCRIPTION
    检测当前控制台编码设置，提供交互式一键修复
.USAGE
    .\Fix-ConsoleEncoding.ps1
#>

$ErrorActionPreference = 'Continue'

function Write-Status {
    param([string]$Label, [string]$Value, [bool]$Ok)
    $icon = if ($Ok) { '✅' } else { '❌' }
    $color = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  {0} {1}: " -f $icon, $Label) -NoNewline
    Write-Host $Value -ForegroundColor $color
}

function Test-ConsoleEncoding {
    Write-Host "`n========== 控制台编码检测 ==========" -ForegroundColor Cyan

    $codePage = (chcp) -replace '.*?(\d+).*', '$1'
    Write-Status -Label "控制台代码页 (chcp)" -Value $codePage -Ok ($codePage -eq '65001')

    $consoleOut = [Console]::OutputEncoding.CodePage
    Write-Status -Label "[Console]::OutputEncoding" -Value $consoleOut -Ok ($consoleOut -eq 65001)

    $consoleIn = [Console]::InputEncoding.CodePage
    Write-Status -Label "[Console]::InputEncoding" -Value $consoleIn -Ok ($consoleIn -eq 65001)

    $psOut = $OutputEncoding.CodePage
    Write-Status -Label '$OutputEncoding' -Value "$psOut ($($OutputEncoding.EncodingName))" -Ok ($psOut -eq 65001)

    $gitQP = git config --get core.quotepath 2>$null
    Write-Status -Label "Git core.quotepath" -Value $gitQP -Ok ($gitQP -eq 'false')

    $gitCommit = git config --get i18n.commitEncoding 2>$null
    Write-Status -Label "Git i18n.commitEncoding" -Value $gitCommit -Ok ($gitCommit -eq 'utf-8')

    $gitLog = git config --get i18n.logOutputEncoding 2>$null
    Write-Status -Label "Git i18n.logOutputEncoding" -Value $gitLog -Ok ($gitLog -eq 'utf-8')

    $regACP = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name ACP).ACP
    $regOEM = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name OEMCP).OEMCP
    $sysUtf8 = ($regACP -eq '65001' -and $regOEM -eq '65001')
    Write-Status -Label "系统代码页 (ACP/OEMCP)" -Value "$regACP/$regOEM" -Ok $sysUtf8

    $betaKey = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name 'ACP' -ErrorAction SilentlyContinue
    $utf8Beta = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\Language' -ErrorAction SilentlyContinue).InstallLanguage -eq 2052
    Write-Status -Label "系统UTF-8 Beta" -Value $(if ($sysUtf8) { '已开启' } else { '未开启' }) -Ok $sysUtf8

    $profileContent = ''
    $profilePath = $PROFILE
    if (Test-Path $profilePath) {
        $profileContent = Get-Content $profilePath -Raw -Encoding UTF8
    }
    $hasProfileFix = $profileContent -match 'OutputEncoding.*UTF8'
    Write-Status -Label '$PROFILE 编码修复' -Value $(if ($hasProfileFix) { '已配置' } else { '未配置' }) -Ok $hasProfileFix

    $sessionOk = ($codePage -eq '65001' -and $consoleOut -eq 65001 -and $consoleIn -eq 65001 -and $psOut -eq 65001 -and $gitQP -eq 'false' -and $gitCommit -eq 'utf-8' -and $gitLog -eq 'utf-8' -and $hasProfileFix)

    return @{
        CodePageOk     = ($codePage -eq '65001')
        ConsoleOutOk   = ($consoleOut -eq 65001)
        ConsoleInOk    = ($consoleIn -eq 65001)
        PsOutOk        = ($psOut -eq 65001)
        GitOk          = ($gitQP -eq 'false' -and $gitCommit -eq 'utf-8' -and $gitLog -eq 'utf-8')
        SysUtf8Ok      = $sysUtf8
        ProfileOk      = $hasProfileFix
        SessionOk      = $sessionOk
        AllOk          = ($sessionOk -and $sysUtf8)
    }
}

function Set-ConsoleEncodingNow {
    Write-Host "`n>>> 修复当前会话编码..." -ForegroundColor Yellow

    chcp 65001 > $null
    Write-Host "  ✅ chcp 65001" -ForegroundColor Green

    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    [Console]::InputEncoding = [System.Text.Encoding]::UTF8
    Write-Host '  ✅ [Console]::Output/InputEncoding = UTF8' -ForegroundColor Green

    $script:OutputEncoding = [System.Text.Encoding]::UTF8
    Write-Host '  ✅ $OutputEncoding = UTF8' -ForegroundColor Green

    git config --global core.quotepath false 2>$null
    git config --global i18n.commitEncoding utf-8 2>$null
    git config --global i18n.logOutputEncoding utf-8 2>$null
    Write-Host '  ✅ Git 编码配置' -ForegroundColor Green
}

function Set-ConsoleEncodingProfile {
    $profilePath = $PROFILE
    $profileDir = Split-Path $profilePath -Parent

    if (-not (Test-Path $profileDir)) {
        New-Item -ItemType Directory -Path $profileDir -Force > $null
    }

    $snippet = @'

# ===== 控制台UTF-8编码修复 (Fix-ConsoleEncoding.ps1 自动添加) =====
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
# ===== END =====

'@

    if (Test-Path $profilePath) {
        $content = Get-Content $profilePath -Raw -Encoding UTF8
        if ($content -match 'OutputEncoding.*UTF8') {
            Write-Host '  ⏭️ $PROFILE 已包含编码修复，跳过' -ForegroundColor DarkGray
            return
        }
        $content += $snippet
        [System.IO.File]::WriteAllBytes($profilePath, [System.Text.Encoding]::UTF8.GetBytes($content))
    } else {
        [System.IO.File]::WriteAllBytes($profilePath, [System.Text.Encoding]::UTF8.GetBytes($snippet))
    }
    Write-Host "  ✅ 已写入 $profilePath" -ForegroundColor Green
}

function Set-SystemUtf8Beta {
    Write-Host ''
    Write-Host '>>> 修复系统级 UTF-8...' -ForegroundColor Yellow
    Write-Host '  此操作修改注册表 ACP/OEMCP 为 65001，需管理员权限，重启后生效。' -ForegroundColor DarkGray
    Write-Host '  如需自动修改注册表，输入 y [默认]；跳过输入 n：' -NoNewline -ForegroundColor Yellow

    $answer = (Read-Host).Trim()
    if ([string]::IsNullOrWhiteSpace($answer)) { $answer = 'y' }

    if ($answer -eq 'y') {
        try {
            Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name ACP -Value '65001'
            Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name OEMCP -Value '65001'
            Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' -Name MACCP -Value '65001'
            Write-Host '  ✅ 注册表已修改，需重启生效' -ForegroundColor Green
        } catch {
            Write-Host "  ❌ 修改注册表失败（需管理员权限）：$_" -ForegroundColor Red
            Write-Host '  💡 请右键 PowerShell 以管理员身份运行后重试' -ForegroundColor Yellow
        }
    } else {
        Write-Host '  ⏭️ 已跳过系统级修复' -ForegroundColor DarkGray
    }
}

# ===== 主流程 =====
try {
    Write-Host ''
    Write-Host '╔══════════════════════════════════════╗' -ForegroundColor Cyan
    Write-Host '║   控制台编码检测与修复工具 v1.0       ║' -ForegroundColor Cyan
    Write-Host '╚══════════════════════════════════════╝' -ForegroundColor Cyan

    $result = Test-ConsoleEncoding

    if ($result.AllOk) {
        Write-Host "`n🎉 所有编码设置正确，无需修复！" -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host '可用操作：' -ForegroundColor Yellow
        Write-Host '  a - 一键修复全部（当前会话 + $PROFILE + Git + 系统UTF-8）[默认]'
        Write-Host '  s - 仅修复当前会话'
        Write-Host '  p - 仅写入 $PROFILE 永久修复'
        Write-Host '  q - 退出'
        Write-Host ''
        Write-Host '请输入选择 [a]: ' -NoNewline -ForegroundColor Yellow

        $choice = (Read-Host).Trim()
        if ([string]::IsNullOrWhiteSpace($choice)) { $choice = 'a' }

        switch ($choice) {
            'a' {
                Set-ConsoleEncodingNow
                Set-ConsoleEncodingProfile
                if (-not $result.SysUtf8Ok) {
                    Set-SystemUtf8Beta
                }
                Write-Host ''
                Write-Host '>>> 验证修复结果...' -ForegroundColor Yellow
                Test-ConsoleEncoding | Out-Null
                Write-Host ''
                Write-Host '✅ 修复完成！新开 PowerShell 窗口即可生效。' -ForegroundColor Green
            }
            's' {
                Set-ConsoleEncodingNow
                Write-Host ''
                Write-Host '✅ 当前会话已修复（关闭窗口后失效）。' -ForegroundColor Green
            }
            'p' {
                Set-ConsoleEncodingProfile
                Write-Host ''
                Write-Host '✅ $PROFILE 已更新，新开 PowerShell 窗口生效。' -ForegroundColor Green
            }
            'q' {
                Write-Host '已退出。'
            }
            default {
                Write-Host '无效选择，已退出。' -ForegroundColor Red
            }
        }
    }
} catch {
    Write-Host "`n❌ 发生错误: $_" -ForegroundColor Red
}

Write-Host ''
Write-Host '按回车键退出...' -NoNewline -ForegroundColor DarkGray
Read-Host
