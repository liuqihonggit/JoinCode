namespace Core.Query;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(StopHooks.StopHookContext))]
[JsonSerializable(typeof(StopHooks.StopHookResult))]
[JsonSerializable(typeof(BudgetAnalysis.DiminishingReturnsResult))]
[JsonSerializable(typeof(UsdBudget.UsdBudgetStatus))]
[JsonSerializable(typeof(UsdBudget.UsdBudgetAlertEventArgs))]
[JsonSerializable(typeof(Snip.SnipOptions))]
[JsonSerializable(typeof(Snip.SnipResult))]
[JsonSerializable(typeof(StateChangedEventArgs<Transitions.QueryState>))]
[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
internal partial class QueryJsonContext : JsonSerializerContext;
