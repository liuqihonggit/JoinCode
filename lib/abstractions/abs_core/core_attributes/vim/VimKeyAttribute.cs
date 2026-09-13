namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class VimKeyAttribute : Attribute
{
    public char Key { get; }

    public VimKeyAttribute(char key)
    {
        Key = key;
    }
}
