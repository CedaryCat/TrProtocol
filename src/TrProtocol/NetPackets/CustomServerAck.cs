using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct CustomServerAck : INetPacket
{
    public readonly MessageID Type => MessageID.CustomServerAck;
    [Int7BitEncoded]
    public int AckId;
}
