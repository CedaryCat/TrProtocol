using TrProtocol.Attributes;
using TrProtocol.Models;
using static Terraria.GameContent.LeashedEntity.NetModule;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetLeashedEntityModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetLeashedEntityModule;

    public MessageType MessageType;

    [Int7BitEncoded]
    public int ID;

    [ConditionEqual(nameof(MessageType), MessageType.FullSync)]
    [ConditionEqual(nameof(MessageType), MessageType.PartialSync)]
    [ExternalMemberValueEqual(nameof(LeashedEntity.FullSync), nameof(MessageType), MessageType.FullSync)]
    public LeashedEntity? Entity;
}
