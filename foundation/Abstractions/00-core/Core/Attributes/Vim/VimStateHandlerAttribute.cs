namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class VimStateHandlerAttribute : Attribute
{
    public string StateTypeName { get; }

    public VimStateHandlerAttribute(string stateTypeName)
    {
        StateTypeName = stateTypeName ?? throw new ArgumentNullException(nameof(stateTypeName));
    }
}
