using System.Buffers;
using System.Buffers.Binary;

namespace TrProtocol;


public enum RentalOwnership
{
    Exclusive,
    Shared
}

public sealed class PacketCodecRental : IDisposable
{
    private readonly RentalOwnership _ownership;
    private byte[]? _buffer;

    internal PacketCodecRental(byte[] buffer, int length, RentalOwnership ownership) 
    {
        _ownership = ownership;
        _buffer = buffer;
        Memory = buffer.AsMemory(0, length);
    }

    public ReadOnlyMemory<byte> Memory { get; private set; }

    public void Dispose() 
    {
        if (_ownership is RentalOwnership.Shared && Interlocked.Exchange(ref _buffer, null) is { } sharedBuffer) 
        {
            ArrayPool<byte>.Shared.Return(sharedBuffer, clearArray: false);
            Memory = ReadOnlyMemory<byte>.Empty;
        }
        else if (_buffer is { } localBuffer) 
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(localBuffer, clearArray: false);
            Memory = ReadOnlyMemory<byte>.Empty;
        }
    }
}

public static class PacketCodec
{
    public const int MaxPacketSize = ushort.MaxValue;

    private static int SerializeUnchecked(INetPacket packet, Span<byte> destination)
    {
        var payloadBuffer = destination[2..];
        int contentLength;

        unsafe
        {
            fixed (byte* pPayload = payloadBuffer)
            {
                void* ptr = pPayload;
                packet.WriteContent(ref ptr);
                contentLength = (int)((byte*)ptr - pPayload);
            }
        }

        var totalLength = contentLength + 2;
        BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)totalLength));
        return totalLength;
    }

    public static int Serialize(INetPacket packet, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (destination.Length < MaxPacketSize)
        {
            throw new ArgumentException(
                $"Destination buffer must be at least {MaxPacketSize} bytes.",
                nameof(destination));
        }

        return SerializeUnchecked(packet, destination);
    }

    public static int SerializeDirect(INetPacket packet, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(writer);

        var destination = writer.GetSpan(MaxPacketSize);
        var totalLength = SerializeUnchecked(packet, destination);
        writer.Advance(totalLength);
        return totalLength;
    }

    public static int Serialize(INetPacket packet, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var scratchBuffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);
        try
        {
            var totalLength = Serialize(packet, scratchBuffer);
            var output = writer.GetSpan(totalLength);
            scratchBuffer.AsSpan(0, totalLength).CopyTo(output);
            writer.Advance(totalLength);
            return totalLength;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(scratchBuffer, clearArray: false);
            throw;
        }
    }

    public static PacketCodecRental SerializeRented(INetPacket packet, RentalOwnership ownership = RentalOwnership.Exclusive) 
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);
        try 
        {
            var totalLength = Serialize(packet, buffer);
            return new PacketCodecRental(buffer, totalLength, ownership);
        }
        catch 
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            throw;
        }
    }

    public static INetPacket Deserialize(ReadOnlySpan<byte> packetData, bool client)
    {
        if (packetData.Length < 2)
            throw new InvalidOperationException("Packet header is incomplete.");

        var totalLength = BinaryPrimitives.ReadUInt16LittleEndian(packetData);
        if (totalLength < 2 || totalLength > packetData.Length)
            throw new InvalidOperationException($"Invalid packet length: {totalLength}");

        var payloadLength = totalLength - 2;
        var payload = packetData.Slice(2, payloadLength);

        unsafe
        {
            fixed (byte* pPayload = payload)
            {
                void* ptr = pPayload;
                byte* end = pPayload + payloadLength;
                return INetPacket.ReadINetPacket(ref ptr, end, !client);
            }
        }
    }
}
