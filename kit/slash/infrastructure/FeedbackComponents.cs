namespace JoinCode.Cli;

// ─── FeedbackState / FeedbackRenderer / FeedbackStep / FeedbackRedactor ───

/// <summary>
/// 反馈状态
/// </summary>
public sealed class FeedbackState
{
    /// <summary>
    /// 当前反馈步骤
    /// </summary>
    public FeedbackStep Step { get; init; }

    /// <summary>
    /// 反馈描述内容
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 是否反馈成功
    /// </summary>
    public bool IsSuccess { get; init; }
}

/// <summary>
/// 反馈步骤
/// </summary>
public enum FeedbackStep
{
    /// <summary>
    /// 用户输入阶段
    /// </summary>
    [EnumValue("userInput")]
    UserInput,

    /// <summary>
    /// 评分阶段
    /// </summary>
    [EnumValue("rating")]
    Rating,

    /// <summary>
    /// 评论阶段
    /// </summary>
    [EnumValue("comment")]
    Comment,

    /// <summary>
    /// 确认阶段
    /// </summary>
    [EnumValue("confirm")]
    Confirm,

    /// <summary>
    /// 完成阶段
    /// </summary>
    [EnumValue("done")]
    Done
}

/// <summary>
/// 反馈渲染器 — CLI 简化版
/// </summary>
public sealed class FeedbackRenderer
{
    /// <summary>
    /// 根据反馈状态渲染对应文本
    /// </summary>
    /// <param name="state">反馈状态</param>
    /// <returns>渲染后的文本</returns>
    public string Render(FeedbackState state)
    {
        var sb = new StringBuilder();

        switch (state.Step)
        {
            case FeedbackStep.UserInput:
                sb.AppendLine($"{AnsiStyleEnumConstants.Bold}反馈{AnsiStyleEnumConstants.Reset}");
                sb.AppendLine("请输入您的反馈内容:");
                break;
            case FeedbackStep.Done:
                if (state.IsSuccess)
                {
                    sb.AppendLine($"{TerminalColors.Success}反馈已提交{AnsiStyleEnumConstants.Reset}");
                    if (!string.IsNullOrEmpty(state.Description))
                    {
                        sb.AppendLine($"  内容: {state.Description}");
                    }
                }
                else
                {
                    sb.AppendLine($"{TerminalColors.Error}反馈提交失败{AnsiStyleEnumConstants.Reset}");
                }
                break;
            default:
                sb.AppendLine($"反馈步骤: {state.Step}");
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 显示星级评分，5 星制
    /// </summary>
    /// <param name="rating">评分值，0 到 5</param>
    public static void ShowRating(int rating)
    {
        var stars = new string('★', rating) + new string('☆', 5 - rating);
        TerminalHelper.WriteLine($"  评分: {TerminalColors.Warning}{stars}{AnsiStyleEnumConstants.Reset}");
    }

    /// <summary>
    /// 显示评论内容，空白时不输出
    /// </summary>
    /// <param name="comment">评论文本</param>
    public static void ShowComment(string comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            TerminalHelper.WriteLine($"  评论: {comment}");
        }
    }
}

/// <summary>
/// 反馈脱敏器
/// </summary>
public static class FeedbackRedactor
{
    /// <summary>
    /// 对文本进行脱敏处理，当前实现原样返回
    /// </summary>
    /// <param name="text">待脱敏文本</param>
    /// <returns>脱敏后的文本</returns>
    public static string Redact(string text)
    {
        return text;
    }
}
