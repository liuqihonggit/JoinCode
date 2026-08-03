
namespace Core.Skills.ExpressionMethods;

public sealed class AbsMethod : IExpressionMethod
{
    public string[] Names => new[] { "abs" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

public sealed class RoundMethod : IExpressionMethod
{
    public string[] Names => new[] { "round" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            var decimals = args.Count >= 1 && int.TryParse(elementToString(args[0]), out var d) ? d : 0;
            return Math.Round(value, decimals).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

public sealed class FloorMethod : IExpressionMethod
{
    public string[] Names => new[] { "floor" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Floor(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

public sealed class CeilingMethod : IExpressionMethod
{
    public string[] Names => new[] { "ceiling", "ceil" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Ceiling(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

public sealed class MaxMethod : IExpressionMethod
{
    public string[] Names => new[] { "max" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 1 &&
            double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value1) &&
            double.TryParse(elementToString(args[0]), NumberStyles.Any, CultureInfo.InvariantCulture, out var value2))
        {
            return Math.Max(value1, value2).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

public sealed class MinMethod : IExpressionMethod
{
    public string[] Names => new[] { "min" };
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
    {
        if (args.Count >= 1 &&
            double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value1) &&
            double.TryParse(elementToString(args[0]), NumberStyles.Any, CultureInfo.InvariantCulture, out var value2))
        {
            return Math.Min(value1, value2).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}
