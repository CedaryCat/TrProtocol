using TrProtocol.Attributes;
using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TEDisplayDollItemSync : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.TEDisplayDollItemSync;
    public byte PlayerSlot { get; set; }
    public int TileEntityID;
    public byte ItemSlot;
    public DisplayDollSyncCommandTypes Command;
    [ConditionEqual(nameof(Command), DisplayDollSyncCommandTypes.SyncPose)]
    public byte Pose;
    [ConditionNotEqual(nameof(Command), DisplayDollSyncCommandTypes.SyncPose)]
    public ushort ItemID;
    [ConditionNotEqual(nameof(Command), DisplayDollSyncCommandTypes.SyncPose)]
    public ushort Stack;
    [ConditionNotEqual(nameof(Command), DisplayDollSyncCommandTypes.SyncPose)]
    public byte Prefix;
}