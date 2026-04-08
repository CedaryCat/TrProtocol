using System.Buffers;
using Terraria;

namespace TrProtocol;

public class PacketSerializer(bool client)
{
    public bool Client { get; } = client;
    
    public byte[] Serialize(INetPacket p)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        bw.Write((ushort)0);

        var tempBuffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);
        int contentLen;

        unsafe
        {
            fixed (byte* pTemp = tempBuffer)
            {
                void* ptr = pTemp;
                p.WriteContent(ref ptr);
                contentLen = (int)((byte*)ptr - pTemp);
            }
        }
        
        bw.BaseStream.Position = 0;
        bw.Write((ushort)(contentLen + 2));
        bw.Write(tempBuffer, 0, contentLen);

        var data = ms.ToArray();
        ArrayPool<byte>.Shared.Return(tempBuffer);
        return data;
    }

    public INetPacket Deserialize(BinaryReader br0)
    {
        ushort totalLength = br0.ReadUInt16();
     
        int payloadLen = totalLength - 2;
        if (payloadLen < 0) throw new Exception("Invalid packet length");

        byte[] payload = br0.ReadBytes(payloadLen);
        
        unsafe
        {
            fixed (byte* pPayload = payload)
            {
                void* ptr = pPayload;
                byte* end = pPayload + payloadLen;
                
                return INetPacket.ReadINetPacket(ref ptr, end, !Client);
            }
        }
    }
}