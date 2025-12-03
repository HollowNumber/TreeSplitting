using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;
using TreeSplitting.Item;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlock : Block
{
    WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api.Side != EnumAppSide.Client) return;
        
        interactions = ObjectCacheUtil.GetOrCreate(api, "choppingblock-interactions", () =>
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
                    MouseButton = EnumMouseButton.Left, // Capture Left Click
                    Itemstacks = toolStacks.ToArray(), // Allow any item (we check in BE) or specify Axes here
                    GetMatchingStacks = (wi, bs, es) => {
                        // Only capture if hitting a voxel
                        BEChoppingBlock be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BEChoppingBlock;
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
    
    public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return GetSelectionBoxes(blockAccessor, pos);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
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