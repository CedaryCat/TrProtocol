namespace Terraria.GameContent;

public abstract class LeashedEntity
{
    public class NetModule
    {
        public enum MessageType : byte
        {
            Remove,
            FullSync,
            PartialSync
        }
    }
}
