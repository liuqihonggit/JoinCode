namespace JoinCode.Abstractions.LLM;

public enum ToolChoice {
    [EnumValue("none")]
    None,
    [EnumValue("auto_invoke")]
    AutoInvoke
}