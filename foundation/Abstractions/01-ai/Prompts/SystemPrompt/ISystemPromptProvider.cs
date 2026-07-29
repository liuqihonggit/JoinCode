namespace JoinCode.Abstractions.Prompts;

/// <summary>
/// 系统提示词提供者接口 - 用于动态生成提示词内容
/// </summary>
public interface ISystemPromptProvider {
    /// <summary>
    /// 获取提示词部分
    /// </summary>
    IEnumerable<SystemPromptSection> GetSections();
}
