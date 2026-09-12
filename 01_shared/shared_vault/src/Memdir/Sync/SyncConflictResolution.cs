
namespace Memdir.Sync;

public enum SyncConflictResolution
{
    [EnumValue("keepLocal")]
    KeepLocal,
    [EnumValue("keepRemote")]
    KeepRemote,
    [EnumValue("keepNewest")]
    KeepNewest,
    [EnumValue("merge")]
    Merge
}
