using Terraria.DataStructures;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

[PolymorphicBase(typeof(LeashedEntityPrototype), nameof(Prototype))]
public abstract partial class LeashedEntity : IAutoSerializable
{
    [Int7BitEncoded]
    public abstract LeashedEntityPrototype Prototype { get; }

    [ExternalMember]
    [IgnoreSerialize]
    public bool FullSync;
    public abstract Point16 AnchorPosition { get; set; }
    public abstract unsafe void ReadContent(ref void* ptr, void* endPtr);
    public abstract unsafe void WriteContent(ref void* ptr);
}
