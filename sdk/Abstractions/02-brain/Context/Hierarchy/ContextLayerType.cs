namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

[JsonConverter(typeof(JsonStringEnumConverter<ContextLayerType>))]
public enum ContextLayerType
{
    [EnumValue("detailed")] Detailed = 0,

    Summary = 1,

    [EnumValue("index")] Index = 2
}
