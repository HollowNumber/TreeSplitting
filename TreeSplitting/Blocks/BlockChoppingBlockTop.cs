using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlockTop : Block
{
    WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api.Side != EnumAppSide.Client) return;

        interactions = new WorldInteraction[] {
            new WorldInteraction()
            {
                ActionLangCode = "treesplitting-chop",
                MouseButton = EnumMouseButton.Left, // Capture Left Click
                Itemstacks = null, // Allow any item (we check in BE) or specify Axes here
                GetMatchingStacks = (wi, bs, es) => {
                    // Only capture if hitting a voxel
                    if (bs.SelectionBoxIndex > 0) return wi.Itemstacks;
                    return null;
                }
            }
        };
    }
    
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        // Fix this at some point
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
    
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
    {
        if (world.GetBlockEntity(pos.DownCopy()) is BEChoppingBlock be)
        {
            return be.GetTopBlockBoxes(); 
        }
        return new Cuboidf[0];
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor world, BlockPos pos)
    {
        return new Cuboidf[0];
    }

    public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt,
        int counter)
    {
        if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position.DownCopy()) is BEChoppingBlock be)
        {
            
            if (counter == 0)
            {
                be.OnTopBlockLeftClick(player, blockSel.SelectionBoxIndex);
            }
            return 9999999f; 
        }
        return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BlockPos downPos = blockSel.Position.DownCopy();
        if (world.BlockAccessor.GetBlockEntity(downPos) is BEChoppingBlock be)
        {
            return be.OnInteract(byPlayer, blockSel);
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
    {
        return true;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        // Use standard "Break the bottom block too" logic
        BlockPos downPos = pos.DownCopy();
        Block bottomBlock = world.BlockAccessor.GetBlock(downPos);
        if (bottomBlock.Code.Path.Contains("choppingblock"))
        {
            world.BlockAccessor.BreakBlock(downPos, byPlayer);
        }
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
    
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return null;
    }
}