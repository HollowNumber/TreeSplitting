using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;
using TreeSplitting.Item;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlockTop : Block
{
    
    WorldInteraction[] interactions;

    

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api.Side != EnumAppSide.Client) return;
        
        
        interactions = ObjectCacheUtil.GetOrCreate(api, "choppingblocktop-interactions", () =>
        {
            List<ItemStack> toolStacks = [];

            foreach (Vintagestory.API.Common.Item worldItem in api.World.Items)
            {
                if (worldItem.Code == null) continue;
                
                if (worldItem is ItemAxe or ItemSaw or ItemChisel) toolStacks.Add(new ItemStack(worldItem));
            }
            
            return new[] {
                new WorldInteraction()
                {
                    ActionLangCode = "treesplitting-chop",
                    MouseButton = EnumMouseButton.Left, 
                    Itemstacks = toolStacks.ToArray(), 
                    GetMatchingStacks = (wi, bs, es) => {
                        BEChoppingBlock be = api.World.BlockAccessor.GetBlockEntity(bs.Position.DownCopy()) as BEChoppingBlock;
                        return be?.WorkItemStack == null ? null : wi.Itemstacks;
                    }
                }
            };
        });

    }
    
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
    
    
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
    {
        if (world.GetBlockEntity(pos.DownCopy()) is BEChoppingBlock be)
        {
            return be.GetTopBlockBoxes(); 
        }
        return [];
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor world, BlockPos pos)
    {
        return [];
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
        return null!;
    }
}