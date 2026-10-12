using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

/// <summary>
/// Invisible block reserving the space above the chopping block for the work item.
/// It only reports the part of the work item inside its own cell; the stump reports the rest.
/// </summary>
public class BlockChoppingBlockTop : Block
{
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos.DownCopy()) is BEChoppingBlock be)
        {
            return be.TopSelectionBoxes;
        }
        return base.GetSelectionBoxes(blockAccessor, pos);
    }


    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos.DownCopy()) is BEChoppingBlock be)
        {
            return be.TopCollisionBoxes;
        }
        return base.GetCollisionBoxes(blockAccessor, pos);
    }

    // Nothing can hang off or rest on the empty space above the stump
    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi? attachmentArea = null)
    {
        return false;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        BlockPos botPos = pos.DownCopy();
        if (world.BlockAccessor.GetBlock(botPos) is BlockChoppingBlock)
        {
            world.BlockAccessor.BreakBlock(botPos, byPlayer);
        }
        else
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BlockPos botPos = blockSel.Position.DownCopy();
        if (world.BlockAccessor.GetBlockEntity(botPos) is BEChoppingBlock be)
        {
            be.OnInteract(byPlayer, blockSel);

            // Always consume the click so a held block isn't placed against the log
            return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [];
    }
}
