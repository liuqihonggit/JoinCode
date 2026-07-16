namespace JoinCode.ChatCommands;

public abstract class ToggleCommandBase : ChatCommandBase
{
    protected virtual string ArgumentHintText => "[on|off]";

    public override string ArgumentHint => ArgumentHintText;

    protected virtual ToggleAction? ResolveToggleAction(string args)
        => ToggleActionExtensions.FromValue(args);

    protected virtual ToggleNullAction NullAction => ToggleNullAction.Toggle;

    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = GetNormalizedArgs(context);
        var action = ResolveToggleAction(args);

        switch (action)
        {
            case ToggleAction.On:
                await OnEnabledAsync(context).ConfigureAwait(false);
                break;
            case ToggleAction.Off:
                await OnDisabledAsync(context).ConfigureAwait(false);
                break;
            default:
                await OnDefaultAsync(context, args).ConfigureAwait(false);
                break;
        }

        await PrintStatusAsync(context).ConfigureAwait(false);

        return ChatCommandResult.Continue();
    }

    protected abstract Task OnEnabledAsync(ChatCommandContext context);

    protected abstract Task OnDisabledAsync(ChatCommandContext context);

    protected virtual Task OnDefaultAsync(ChatCommandContext context, string args)
    {
        if (NullAction == ToggleNullAction.Toggle)
        {
            return OnToggleAsync(context);
        }

        return Task.CompletedTask;
    }

    protected virtual Task OnToggleAsync(ChatCommandContext context) => Task.CompletedTask;

    protected virtual Task PrintStatusAsync(ChatCommandContext context) => Task.CompletedTask;
}

public enum ToggleNullAction
{
    Toggle,
    Status,
}
