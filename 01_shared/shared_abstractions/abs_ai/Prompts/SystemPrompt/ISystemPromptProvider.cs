namespace JoinCode.Abstractions.Prompts;

/// <summary>
/// 系统提示词提供者接口 - 生产 SystemPromptSection 列表
/// 关系: IChatPromptManager (02-brain) 消费本接口提供的部分来构建完整提示词（生产者-消费者）
/// </summary>
public interface ISystemPromptProvider {
    /// <summary>
    /// 获取提示词部分
    /// </summary>
    IEnumerable<SystemPromptSection> GetSections();
}
