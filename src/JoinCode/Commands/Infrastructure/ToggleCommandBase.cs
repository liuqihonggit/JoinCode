namespace JoinCode.ChatCommands;

public abstract class ToggleCommandBase : ChatCommandBase
{
    protected virtual string ArgumentHintText => "[on|off]";

    public override string ArgumentHint => ArgumentHintText;

    protected virtual ToggleAction? ResolveToggleAction(string args)
        => ToggleActionExtensions.FromValue(args);

    protected virtual ToggleNullAction NullAction => ToggleNullAction.Toggle;

    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = GetNormalizedArgs(context);
        var action = ResolveToggleAction(args);

        switch (action)
        {
            case ToggleAction.On:
                OnEnabled(context);
                break;
            case ToggleAction.Off:
                OnDisabled(context);
                break;
            default:
                OnDefault(context, args);
                break;
        }

        PrintStatus(context);

        return Task.FromResult(ChatCommandResult.Continue());
    }

    protected abstract void OnEnabled(ChatCommandContext context);

    protected abstract void OnDisabled(ChatCommandContext context);

    protected virtual void OnDefault(ChatCommandContext context, string args)
    {
        if (NullAction == ToggleNullAction.Toggle)
        {
            OnToggle(context);
        }
    }

    protected virtual void OnToggle(ChatCommandContext context) { }

    protected abstract void PrintStatus(ChatCommandContext context);
}

public enum ToggleNullAction
{
    Toggle,
    Status,
}
