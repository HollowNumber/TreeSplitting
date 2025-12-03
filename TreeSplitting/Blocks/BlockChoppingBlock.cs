using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlock : Block
{
    // ============================================================================
    // PLACEMENT LOGIC
    // ============================================================================

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        // 1. Check if there is space UP for the top block
        BlockPos upPos = blockSel.Position.UpCopy();
        Block upBlock = world.BlockAccessor.GetBlock(upPos);

        if (!upBlock.IsReplacableBy(this))
        {
            failureCode = "notenoughspace";
            return false;
        }

        // 2. Default placement check (ground check etc)
        if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

        return true;
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);

        BlockPos upPos = blockPos.UpCopy();
        Block topBlock = world.GetBlock(new AssetLocation("treesplitting:choppingblocktop")); 
        
        if (topBlock != null)
        {
            world.BlockAccessor.SetBlock(topBlock.BlockId, upPos);
        }
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        // Only intercept if in Creative and hitting a Voxel
        if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative && byPlayer.CurrentBlockSelection.SelectionBoxIndex > 0)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BEChoppingBlock be)
            {
                be.OnPlayerLeftClick(byPlayer, byPlayer.CurrentBlockSelection);
            }
            
            world.BlockAccessor.GetChunkAtBlockPos(pos).MarkModified();
            return; 
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        
        BlockPos upPos = pos.UpCopy();
        if (world.BlockAccessor.GetBlock(upPos) is BlockChoppingBlockTop)
        {
            world.BlockAccessor.SetBlock(0, upPos);
        }
    }


    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
    {
        if (world.GetBlockEntity(pos) is BEChoppingBlock be) return be.SelectionBoxes;
        return base.GetSelectionBoxes(world, pos);
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor world, BlockPos pos)
    {
        if (world.GetBlockEntity(pos) is BEChoppingBlock be) return be.CollisionBoxes;
        return base.GetCollisionBoxes(world, pos);
    }

    public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt,
        int counter)
    {
        if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BEChoppingBlock be)
        {
            if (blockSel.SelectionBoxIndex > 0)
            {
                if (counter == 0) be.OnPlayerLeftClick(player, blockSel);
                return 9999999f; 
            }
        }
        api.Logger.Error("Outside of selection box index");
        return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
    }


    public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
    {
        return true;
    }


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEChoppingBlock be) return be.OnInteract(byPlayer, blockSel);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}