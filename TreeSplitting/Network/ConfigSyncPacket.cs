using ProtoBuf;

namespace TreeSplitting.Network;

// The server's say on how the axe works, so players don't go by their own config file
[ProtoContract]
public class ConfigSyncPacket
{
    [ProtoMember(1)] public int HitsPerPlane;
}
