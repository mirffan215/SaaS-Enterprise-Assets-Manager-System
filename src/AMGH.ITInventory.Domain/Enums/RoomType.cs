namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum RoomType
{
    Office,
    ServerRoom,
    DataCenter,
    Store,
    MeetingRoom,
    Other
}
