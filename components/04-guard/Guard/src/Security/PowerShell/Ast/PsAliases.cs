namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS 常用别名 → 规范 cmdlet 名映射
/// 使用 Object.Create(null) 等价模式防止原型链污染
/// </summary>
public static class PsAliases
{
    /// <summary>
    /// 尝试解析别名为规范 cmdlet 名
    /// </summary>
    public static bool TryResolve(string alias, out string canonical)
    {
        if (AliasMap.TryGetValue(alias.ToLowerInvariant(), out var result))
        {
            canonical = result;
            return true;
        }
        canonical = string.Empty;
        return false;
    }

    /// <summary>
    /// 获取规范名（如果找不到返回原始名的小写形式）
    /// </summary>
    public static string ResolveToCanonical(string name)
    {
        var lower = name.ToLowerInvariant();
        return AliasMap.TryGetValue(lower, out var canonical) ? canonical : lower;
    }

    /// <summary>
    /// 别名映射表 — 与 TS COMMON_ALIASES 1:1 对齐
    /// </summary>
    private static readonly FrozenDictionary<string, string> AliasMap = BuildAliasMap();

    private static FrozenDictionary<string, string> BuildAliasMap()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 目录列表
            ["ls"] = "Get-ChildItem",
            ["dir"] = "Get-ChildItem",
            ["gci"] = "Get-ChildItem",
            // 内容
            ["cat"] = "Get-Content",
            ["type"] = "Get-Content",
            ["gc"] = "Get-Content",
            // 导航
            ["cd"] = "Set-Location",
            ["sl"] = "Set-Location",
            ["chdir"] = "Set-Location",
            ["pushd"] = "Push-Location",
            ["popd"] = "Pop-Location",
            ["pwd"] = "Get-Location",
            ["gl"] = "Get-Location",
            // 项操作
            ["gi"] = "Get-Item",
            ["gp"] = "Get-ItemProperty",
            ["ni"] = "New-Item",
            ["mkdir"] = "New-Item",
            ["md"] = "New-Item",
            ["ri"] = "Remove-Item",
            ["del"] = "Remove-Item",
            ["rd"] = "Remove-Item",
            ["rmdir"] = "Remove-Item",
            ["rm"] = "Remove-Item",
            ["erase"] = "Remove-Item",
            ["mi"] = "Move-Item",
            ["mv"] = "Move-Item",
            ["move"] = "Move-Item",
            ["ci"] = "Copy-Item",
            ["cp"] = "Copy-Item",
            ["copy"] = "Copy-Item",
            ["cpi"] = "Copy-Item",
            ["si"] = "Set-Item",
            ["rni"] = "Rename-Item",
            ["ren"] = "Rename-Item",
            // 进程
            ["ps"] = "Get-Process",
            ["gps"] = "Get-Process",
            ["kill"] = "Stop-Process",
            ["spps"] = "Stop-Process",
            ["start"] = "Start-Process",
            ["saps"] = "Start-Process",
            ["sajb"] = "Start-Job",
            ["ipmo"] = "Import-Module",
            // 输出
            ["echo"] = "Write-Output",
            ["write"] = "Write-Output",
            ["sleep"] = "Start-Sleep",
            // 帮助
            ["help"] = "Get-Help",
            ["man"] = "Get-Help",
            ["gcm"] = "Get-Command",
            // 服务
            ["gsv"] = "Get-Service",
            // 变量
            ["gv"] = "Get-Variable",
            ["sv"] = "Set-Variable",
            // 历史
            ["h"] = "Get-History",
            ["history"] = "Get-History",
            // 调用
            ["iex"] = "Invoke-Expression",
            ["iwr"] = "Invoke-WebRequest",
            ["irm"] = "Invoke-RestMethod",
            ["icm"] = "Invoke-Command",
            ["ii"] = "Invoke-Item",
            // PSSession
            ["nsn"] = "New-PSSession",
            ["etsn"] = "Enter-PSSession",
            ["exsn"] = "Exit-PSSession",
            ["gsn"] = "Get-PSSession",
            ["rsn"] = "Remove-PSSession",
            // 杂项
            ["cls"] = "Clear-Host",
            ["clear"] = "Clear-Host",
            ["select"] = "Select-Object",
            ["where"] = "Where-Object",
            ["foreach"] = "ForEach-Object",
            ["%"] = "ForEach-Object",
            ["?"] = "Where-Object",
            ["measure"] = "Measure-Object",
            ["ft"] = "Format-Table",
            ["fl"] = "Format-List",
            ["fw"] = "Format-Wide",
            ["oh"] = "Out-Host",
            ["ogv"] = "Out-GridView",
            ["ac"] = "Add-Content",
            ["clc"] = "Clear-Content",
            ["tee"] = "Tee-Object",
            ["epcsv"] = "Export-Csv",
            ["sp"] = "Set-ItemProperty",
            ["rp"] = "Remove-ItemProperty",
            ["cli"] = "Clear-Item",
            ["epal"] = "Export-Alias",
            ["sls"] = "Select-String",
        };

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
