using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

/// <summary>
/// Old invisible block above the stump. Not placed anymore, only kept so old worlds load, it gets swapped for the vanilla multiblock one.
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

    // Only highlight the voxel you're looking at like the anvil, otherwise the black outline covers the orange one
    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
    {
        return true;
    }

    // Nothing should attach to the space above the stump
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

            // Always eat the click so you can't place blocks on the log
            return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [];
    }
}
