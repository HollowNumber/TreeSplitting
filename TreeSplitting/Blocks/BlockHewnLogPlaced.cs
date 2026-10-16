using TreeSplitting.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Blocks;

/// <summary>
/// Partly hewn log sitting in the world, the block entity draws it. Breaking it gives the item back as it was.
/// </summary>
public class BlockHewnLogPlaced : Block
{
    public const string BlockCode = "treesplitting:hewnlogplaced";

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        blockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced be ? be.Boxes : base.GetSelectionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        blockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced be ? be.Boxes : base.GetCollisionBoxes(blockAccessor, pos);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return world.BlockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced { Piece: { } piece } ? [piece.Clone()] : [];
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return world.BlockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced { Piece: { } piece } ? piece.Clone() : base.OnPickBlock(world, pos);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        return world.BlockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced { Piece: { } piece } ? piece.GetName() : base.GetPlacedBlockName(world, pos);
    }
}
