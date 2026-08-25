using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Terraria.DataStructures;
using TrProtocol.Attributes;

namespace TrProtocol.Models.LeashedEntities;

public partial class FishLeashedCritter : LeashedEntity
{
    public sealed override LeashedEntityPrototype Prototype => LeashedEntityPrototype.FishLeashedCritter;
    [Condition(nameof(FullSync))]
    public sealed override Point16 AnchorPosition { get; set; }

    [Condition(nameof(FullSync))]
    [Int7BitEncoded]
    public int NPCType;
    [Condition(nameof(FullSync))]
    public Vector2 Size;

    public HalfVector2 OffsetFromAnchor;
    public bool Direction;
    public uint RandState;
    public short WaitTime;
    public byte State;
    public sbyte TargetXFromAnchor;
    public sbyte TargetYFromAnchor;
}
