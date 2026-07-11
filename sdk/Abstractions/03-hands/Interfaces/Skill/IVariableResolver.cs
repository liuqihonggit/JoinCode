namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 变量解析器接口 — 支持嵌套变量、默认值和表达式
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// 解析并替换字符串中的变量
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="variables">变量字典</param>
    /// <param name="throwOnMissing">变量不存在时是否抛出异常</param>
    /// <returns>替换后的字符串</returns>
    string Resolve(string input, Dictionary<string, JsonElement> variables, bool throwOnMissing = false);
}
