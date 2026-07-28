namespace Core.Skills;

/// <summary>
/// 技能参数验证中间件 — 检查必填参数是否提供
/// </summary>
[Register]
public sealed partial class SkillValidationMiddleware : ISkillMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 SkillValidationMiddleware
    /// </summary>
    public SkillValidationMiddleware() { }

    /// <inheritdoc />
    public Task InvokeAsync(SkillContext context, MiddlewareDelegate<SkillContext> next, CancellationToken ct)
    {
        if (context.Skill == null)
        {
            return next(context, ct);
        }

        var parameters = context.Parameters ?? new Dictionary<string, JsonElement>();

        foreach (var (name, param) in context.Skill.Parameters)
        {
            if (param.Required && !parameters.ContainsKey(name))
            {
                context.ValidationError = L.T(StringKey.SkillServiceMissingRequiredParam, name);
                context.Result = SkillResult.FailureResult(context.SkillName, context.ValidationError);
                return Task.CompletedTask; // 短路
            }
        }

        return next(context, ct);
    }
}
