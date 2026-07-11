
namespace Core.Skills.ExpressionMethods;

public sealed class ToUpperMethod : IExpressionMethod
{
    public string[] Names => ["toupper", "touppercase"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.ToUpperInvariant();
}

public sealed class ToLowerMethod : IExpressionMethod
{
    public string[] Names => ["tolower", "tolowercase"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.ToLowerInvariant();
}

public sealed class TrimMethod : IExpressionMethod
{
    public string[] Names => ["trim"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.Trim();
}

public sealed class TrimStartMethod : IExpressionMethod
{
    public string[] Names => ["trimstart"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.TrimStart();
}

public sealed class TrimEndMethod : IExpressionMethod
{
    public string[] Names => ["trimend"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.TrimEnd();
}

public sealed class SubstringMethod : IExpressionMethod
{
    public string[] Names => ["substring"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 1 && int.TryParse(elementToString(args[0]), out var startIndex))
        {
            var length = args.Count >= 2 && int.TryParse(elementToString(args[1]), out var len) ? len : target.Length - startIndex;
            if (startIndex >= 0 && startIndex < target.Length)
            {
                length = Math.Min(length, target.Length - startIndex);
                return target.Substring(startIndex, length);
            }
        }
        return target;
    }
}

public sealed class ReplaceMethod : IExpressionMethod
{
    public string[] Names => ["replace"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 2)
        {
            return target.Replace(elementToString(args[0]), elementToString(args[1]));
        }
        return target;
    }
}

public sealed class ContainsMethod : IExpressionMethod
{
    public string[] Names => ["contains"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.Contains(elementToString(args[0])).ToString() : "false";
}

public sealed class StartsWithMethod : IExpressionMethod
{
    public string[] Names => ["startswith"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.StartsWith(elementToString(args[0])).ToString() : "false";
}

public sealed class EndsWithMethod : IExpressionMethod
{
    public string[] Names => ["endswith"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.EndsWith(elementToString(args[0])).ToString() : "false";
}

public sealed class IndexOfMethod : IExpressionMethod
{
    public string[] Names => ["indexof"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.IndexOf(elementToString(args[0]), StringComparison.Ordinal).ToString() : "-1";
}

public sealed class LengthMethod : IExpressionMethod
{
    public string[] Names => ["length"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.Length.ToString();
}

public sealed class SplitMethod : IExpressionMethod
{
    public string[] Names => ["split"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 1)
        {
            var separator = elementToString(args[0]);
            var parts = target.Split(separator);
            return string.Join(", ", parts);
        }
        return target;
    }
}

public sealed class FormatMethod : IExpressionMethod
{
    public string[] Names => ["format"];
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 1)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, target, args.Select(elementToString).Cast<object>().ToArray());
            }
            catch
            {
                return target;
            }
        }
        return target;
    }
}
