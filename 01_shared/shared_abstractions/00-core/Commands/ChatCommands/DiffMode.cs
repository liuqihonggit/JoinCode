namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /diff 命令子模式集合。
/// 适用范围: /diff [files|cached|staged]
/// 3 个值全 Diff 专属,与 ToggleAction/CrudAction 无重叠,创建独立枚举。
///
/// 使用示例:
/// - FromValue("files")  → DiffMode.Files
/// - FromValue("STAGED") → DiffMode.Staged (OrdinalIgnoreCase 别名)
/// - DiffMode.Cached.ToValue() → "cached"
/// </summary>
public enum DiffMode
{
    /// <summary>列出变更文件(Modified/Staged/Untracked)</summary>
    [EnumValue("files")] Files,

    /// <summary>显示已暂存(Staged)的 diff 内容</summary>
    [EnumValue("cached")] Cached,

    /// <summary>显示已暂存 diff 内容(staged 是 cached 的 Git 术语别名)</summary>
    [EnumValue("staged")] Staged,
}
