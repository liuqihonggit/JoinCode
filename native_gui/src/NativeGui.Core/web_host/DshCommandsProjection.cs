namespace JoinCode.Gui.WebHost;

/// <summary>
/// 斜杠命令元数据 → DSH 命令目录投影（TASK058 B5，翻译单点）：
/// 引擎 <see cref="SlashCommandMetadata"/>（Name 含 / 前缀）→ 前端 <c>/{name}</c> 组装约定（去前缀）；
/// <see cref="SlashCommandMetadata.ArgumentHint"/> → input.hint（补全提示）。
/// </summary>
internal static class DshCommandsProjection {
    /// <summary>投影单条命令元数据（无参数提示时省略 input）</summary>
    internal static DshCommandInfo Project(SlashCommandMetadata metadata) => new() {
        Name = metadata.Name.TrimStart('/'),
        Description = metadata.Description,
        Input = string.IsNullOrEmpty(metadata.ArgumentHint)
            ? null
            : new DshCommandInputInfo { Hint = metadata.ArgumentHint },
    };
}
