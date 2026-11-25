using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;
using TrProtocol.NetPackets.Mobile;

namespace TrProtocol.NetPackets;

public partial struct PlayerPlatformInfo : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerPlatformInfo;
    public byte PlayerSlot { get; set; }
    public Platform PlatformId;
    public string PlayerId;
    [ConditionEqual(nameof(PlatformId), Platform.Nintendo)]
    public string? PlatformName;
    public bool CountsAsHost;
}
