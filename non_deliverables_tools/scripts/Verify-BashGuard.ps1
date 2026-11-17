#Requires -Version 5.1
<#
.SYNOPSIS
    bash 权限判定的真机验收 —— 对运行中的 jcc mcp_serve 逐条投喂命令，按返回消息判读「命中风险 / 无风险」。

.DESCRIPTION
    单元测试只能证明「给定输入得到预期级别」，证明不了「jcc 装起来之后真的这么判」。
    本脚本把命令矩阵打给真实 jcc.exe 的 MCP HTTP 端点（tools/call 走权限管道），逐条核对结论。

    用法（两步，服务由调用方起，脚本不猜路径、不抢端口）：
      1) jcc mcp_serve --transport http --port 9917 --token verify
      2) pwsh non_deliverables_tools/scripts/Verify-BashGuard.ps1 -Token verify

    判读口径（Auto 模式 + headless 无确认处理器）：
      - 命中风险 → 返回带具体原因的 permission_denied（「已被阻止」「此操作被禁止」等）；
      - 未命中风险 → 落到 DefaultResultMiddleware 的「待确认」，无确认处理器 → PD002「用户拒绝工具执行」。
    因此 PD002 在本脚本里等价于「权限链没判出风险」，不等于「放行后执行成功」：
    headless 下任何待确认都必然被自动拒绝。

    局限（如实记录）：serve 模式的权限管道里没有工具白名单中间件 —— 连默认白名单的 glob 也是待确认，
    所以本脚本验证的是「有没有判出风险」，验证不了「放行后真的执行」。要验证后者必须交互确认。

.PARAMETER BaseUrl
    MCP HTTP 端点，默认 http://localhost:9917/mcp/。

.PARAMETER Token
    mcp_serve 启动时打印的 Mcp-Token。

.EXAMPLE
    pwsh non_deliverables_tools/scripts/Verify-BashGuard.ps1 -Token verify
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:9917/mcp/',
    [Parameter(Mandatory = $true)]
    [string]$Token
)

$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $Token" }

# Expect：no-risk = 权限链不应判出风险；blocked = 必须判出风险
$cases = @(
    # —— flag 作用域：短 flag 在只读命令上不该被当成删除 ——
    [pscustomobject]@{ Cmd = 'echo hello_jcc';                        Expect = 'no-risk'; Why = '基线' }
    [pscustomobject]@{ Cmd = 'ls -R';                                 Expect = 'no-risk'; Why = '-R 递归列目录' }
    [pscustomobject]@{ Cmd = 'sort -r list.txt';                      Expect = 'no-risk'; Why = '-r 逆序' }
    [pscustomobject]@{ Cmd = 'tail -f app.log';                       Expect = 'no-risk'; Why = '-f 跟随文件' }
    [pscustomobject]@{ Cmd = 'grep -rn TODO src';                     Expect = 'no-risk'; Why = '-r 递归搜索' }
    # —— git 只读命令接只读管道 ——
    [pscustomobject]@{ Cmd = 'git log --oneline -5 | head -5';        Expect = 'no-risk'; Why = '两侧都只读' }
    [pscustomobject]@{ Cmd = 'git diff | cat';                        Expect = 'no-risk'; Why = '两侧都只读' }
    [pscustomobject]@{ Cmd = 'git log --oneline | grep fix';          Expect = 'no-risk'; Why = '两侧都只读' }
    [pscustomobject]@{ Cmd = 'cat notes.sh | head -3';                Expect = 'no-risk'; Why = '文件名带 sh 不该误报' }
    [pscustomobject]@{ Cmd = 'ls -la | sort -k5 -n | head -20';       Expect = 'no-risk'; Why = '多段只读管道' }
    # —— 间接执行：管道右侧 / 参数内嵌 ——
    [pscustomobject]@{ Cmd = 'echo hi | xargs rm -rf ./build';        Expect = 'blocked'; Why = 'xargs 参数里的真实命令' }
    [pscustomobject]@{ Cmd = 'find . -name "*.cs" -exec rm -rf {} +'; Expect = 'blocked'; Why = 'find -exec' }
    [pscustomobject]@{ Cmd = 'git log | sh';                          Expect = 'blocked'; Why = '管道进解释器' }
    # —— 破坏性命令仍然要拦 ——
    [pscustomobject]@{ Cmd = 'rm -rf ./build';                        Expect = 'blocked'; Why = '递归删除' }
    [pscustomobject]@{ Cmd = 'rm -rf /';                              Expect = 'blocked'; Why = '整盘' }
    [pscustomobject]@{ Cmd = 'chmod +x run.sh';                       Expect = 'blocked'; Why = '系统修改' }
    # —— AST 升级器：分类器判 Safe，只有 AST 看得出被构造过 ——
    [pscustomobject]@{ Cmd = 'cat /proc/self/environ';                Expect = 'blocked'; Why = 'ProcEnvironAccess' }
    # —— 写脚本类命令不能被 AST 层误伤 ——
    [pscustomobject]@{ Cmd = "cat > /tmp/verify_script.sh <<'EOF'`nrm -rf ./build`nEOF"; Expect = 'no-risk'; Why = 'heredoc 写脚本' }
)

$failures = 0
Write-Host "真机验收 -> $BaseUrl" -ForegroundColor Cyan
Write-Host ''

foreach ($case in $cases) {
    $payload = @{
        jsonrpc = '2.0'
        id      = 1
        method  = 'tools/call'
        params  = @{ name = 'bash'; arguments = @{ command = $case.Cmd } }
    } | ConvertTo-Json -Depth 8 -Compress

    $observed = ''
    $ok = $false
    try {
        $resp = Invoke-RestMethod -Uri $BaseUrl -Method Post -Headers $headers `
            -ContentType 'application/json' -Body $payload -TimeoutSec 60

        $text = $resp.result.content[0].text
        $inner = $null
        try {
            $inner = $text | ConvertFrom-Json
        } catch {
            $observed = 'UNPARSEABLE: ' + $text
            $inner = $null
        }

        if ($null -ne $inner -and $inner.type -eq 'permission_denied') {
            if ($inner.error_code -eq 'PD002') {
                $observed = 'NO-RISK'
                $ok = ($case.Expect -eq 'no-risk')
            } else {
                $observed = 'BLOCKED: ' + $inner.message
                $ok = ($case.Expect -eq 'blocked')
            }
        } elseif ($null -ne $inner) {
            $observed = 'EXECUTED: ' + $text
        }
    } catch {
        $observed = 'ERROR: ' + $_.Exception.Message
    }

    if (-not $ok) {
        $failures++
    }
    if ($observed.Length -gt 66) {
        $observed = $observed.Substring(0, 66)
    }
    $mark = 'OK  '
    $color = 'Green'
    if (-not $ok) {
        $mark = 'FAIL'
        $color = 'Red'
    }
    Write-Host ($mark + ' | ' + $case.Expect.PadRight(8) + ' | ' + $case.Cmd.PadRight(44) + ' | ' + $observed) -ForegroundColor $color
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host "真机验收通过：$($cases.Count) 条全部符合预期" -ForegroundColor Green
    exit 0
}
Write-Host "真机验收失败：$failures / $($cases.Count) 条不符合预期" -ForegroundColor Red
exit 1
