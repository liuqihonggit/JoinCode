
namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子类型枚举 — 替代原 HookTypes 静态常量类
/// </summary>
public enum HookType
{
    [EnumValue("command")] Command,
    [EnumValue("prompt")] Prompt,
    [EnumValue("agent")] Agent,
    [EnumValue("http")] Http,
    [EnumValue("function")] Function,
    [EnumValue("callback")] Callback,
}
