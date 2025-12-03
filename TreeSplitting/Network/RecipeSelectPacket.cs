using ProtoBuf;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Network;

[ProtoContract]
public class RecipeSelectPacket
{
    [ProtoMember(1)] public BlockPos Pos;

    [ProtoMember(2)] public int RecipeId;
}