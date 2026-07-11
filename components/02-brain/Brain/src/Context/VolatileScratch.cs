namespace Core.Context;

public sealed class VolatileScratch
{
    public string? Reasoning { get; set; }
    public Dictionary<string, JsonElement>? PlanState { get; set; }
    public List<string> Notes { get; } = [];

    public void Reset()
    {
        Reasoning = null;
        PlanState = null;
        Notes.Clear();
    }
}
