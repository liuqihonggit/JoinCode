namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class VimStateEntryAttribute : Attribute {
    /// <summary>获取按键。</summary>
    public char Key { get; }

    /// <summary>构造 Vim 状态入口特性。</summary>
    public VimStateEntryAttribute(char key) {
        Key = key;
    }
}