using ProtoBuf;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Network;

// Client to server: the two corners the player marked, or Clear to drop them.
// Server to client: Clear when the saw is done or the log moved, so the marks go away there too
[ProtoContract]
public class SawMarksPacket
{
    [ProtoMember(1)] public BlockPos Pos;
    [ProtoMember(2)] public bool Clear;
    [ProtoMember(3)] public int FaceIndex;
    [ProtoMember(4)] public int X1;
    [ProtoMember(5)] public int Y1;
    [ProtoMember(6)] public int Z1;
    [ProtoMember(7)] public int X2;
    [ProtoMember(8)] public int Y2;
    [ProtoMember(9)] public int Z2;
}
