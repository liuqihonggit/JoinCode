
namespace Core.Configuration;

/// <summary>
/// LlmParameters 扩展方法
/// </summary>
public static class LlmParametersExtensions
{
    /// <summary>
    /// 转换为 ChatOptions
    /// </summary>
    public static ChatOptions ToExecutionSettings(this LlmParameters parameters, ToolChoice toolCallBehavior = ToolChoice.None)
    {
        return new ChatOptions
        {
            Temperature = parameters.Temperature,
            MaxTokens = parameters.MaxTokens,
            TopP = parameters.TopP,
            FrequencyPenalty = parameters.FrequencyPenalty,
            PresencePenalty = parameters.PresencePenalty,
            ToolChoice = toolCallBehavior
        };
    }
}
