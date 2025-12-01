using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlock : Block
{
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ref string failureCode)
    {
        // Check standard placement rules first
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        // Check if the space ABOVE is free for the top part
        BlockPos upPos = blockSel.Position.UpCopy();
        if (!world.BlockAccessor.GetBlock(upPos).IsReplacableBy(this))
        {
            failureCode = "notenoughspace";
            return false;
        }

        return true;
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);

        // Place the invisible Top Block at pos.Up()
        BlockPos upPos = blockPos.UpCopy();
        Block topBlock = world.GetBlock(new AssetLocation("treesplitting:choppingblocktop"));
        if (topBlock != null)
        {
            world.BlockAccessor.SetBlock(topBlock.BlockId, upPos);
        }
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

        // Remove Top Block if it exists
        BlockPos upPos = pos.UpCopy();
        Block upBlock = world.BlockAccessor.GetBlock(upPos);
        if (upBlock.Code.Path == "choppingblocktop")
        {
            world.BlockAccessor.SetBlock(0, upPos); // Set to Air
        }
    }


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BEChoppingBlock? be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEChoppingBlock;
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Delegate to BE
        return be.OnInteract(byPlayer, blockSel);
    }


    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        BEChoppingBlock? be = blockAccessor.GetBlockEntity(pos) as BEChoppingBlock;

        if (be is { SelectionBoxes.Length: > 0 })
        {
            Cuboidf[] combinedBoxes = (Cuboidf[])be.SelectionBoxes.Clone();
            Cuboidf[] stumpBoxes = base.GetSelectionBoxes(blockAccessor, pos);

            if (stumpBoxes.Length > 0)
            {
                combinedBoxes[0] = stumpBoxes[0];
            }
            else
            {
                combinedBoxes[0] = Cuboidf.Default();
            }

            return combinedBoxes;
        }

        return base.GetSelectionBoxes(blockAccessor, pos);
    }


    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BEChoppingBlock be)
        {
            // Ensure collision boxes aren't null
            if (be.CollisionBoxes != null) return be.CollisionBoxes;
        }

        return base.GetCollisionBoxes(blockAccessor, pos);
    }
}