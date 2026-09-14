namespace JoinCode.Abstractions.Models.Runtime;

public enum RuntimeTaskPriority
{
    [EnumValue("now")] Now = 0,
    [EnumValue("next")] Next = 1,
    [EnumValue("later")] Later = 2
}
