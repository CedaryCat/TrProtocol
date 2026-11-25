using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ServerInfo : INetPacket, ISideSpecific
{
    public readonly MessageID Type => MessageID.ServerInfo;
    [S2COnly] public int ListenPort;
    [S2COnly] public string? WorldName;
    [S2COnly] public string? HostName;
    [S2COnly] public ushort MaxTilesX;
    [S2COnly] public bool HasCrimson;
    [S2COnly] public int GameMode;
    [S2COnly] public byte MaxNetPlayers;
    [S2COnly] public byte ActivePlayers;
    [S2COnly] public string? Reserved;
    [S2COnly] public bool PortMapped;
    [S2COnly, Condition(nameof(PortMapped))] public string? ExternalAddress;
    [S2COnly, Condition(nameof(PortMapped))] public int ExternalPort;
}
