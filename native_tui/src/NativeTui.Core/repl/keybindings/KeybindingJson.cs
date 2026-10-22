namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 用户键位 JSON 上下文 — AOT 源生成（ADR 0133：DTO 双向转换，禁止手拼 JSON 字符串）。
/// 文件形状：{"chat": {"chat:externalEditor": ["ctrl+g", "ctrl+x ctrl+e"]}} —
/// 根即「context 小写名 → (action 原名 → 键位串数组)」嵌套字典（动态 context 键决定，无包装字段，
/// 故根 DTO 类型就是嵌套字典本身；context/action 名经 KeybindingNames 大小写不敏感解析）。
/// 宽容解析（注释跳过 + 尾逗号允许）对齐主仓库 RelaxedJsonSerializer 理念（ADR 0042）—
/// 用户手写配置文件常见 // 注释与多余逗号不应直接判死。
/// 序列化同上下文复用（测试与诊断导出用户配置）。
/// </summary>
[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, List<string>>>))]
internal sealed partial class KeybindingJsonContext : JsonSerializerContext;
