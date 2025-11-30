using ProtoBuf;
using TreeSplitting.BlockEntities;
using Vintagestory.API.MathTools;


namespace TreeSplitting.Network;

[ProtoContract]
public class ChopPacket
{
    [ProtoMember(1)] public BlockPos Pos;

    [ProtoMember(2)] public int VoxelX;

    [ProtoMember(3)] public int VoxelY;

    [ProtoMember(4)] public int VoxelZ;
    [ProtoMember(5)] public int FaceIndex;
    [ProtoMember(6)] public EnumToolMode ToolMode;
}