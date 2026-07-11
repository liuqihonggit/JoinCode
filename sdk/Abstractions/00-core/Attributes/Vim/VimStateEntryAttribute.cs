namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class VimStateEntryAttribute : Attribute
{
    public char Key { get; }

    public VimStateEntryAttribute(char key)
    {
        Key = key;
    }
}
