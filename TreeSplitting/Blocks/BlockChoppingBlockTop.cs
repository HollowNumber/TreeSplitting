using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;

namespace TreeSplitting.Blocks;

public class BlockChoppingBlockTop : Block
{
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        BlockPos botPos = pos.DownCopy();
        if (blockAccessor.GetBlockEntity(botPos) is BEChoppingBlock be)
        {
            Cuboidf[] allBoxes = be.SelectionBoxes;
            if (allBoxes == null) return base.GetSelectionBoxes(blockAccessor, pos);

            List<Cuboidf> shiftedBoxes = new List<Cuboidf>();
                
            // Return ALL boxes, shifted down by 1.0
            // This ensures visual continuity (wireframe covers whole log)
            foreach (var box in allBoxes)
            {
                Cuboidf shiftedBox = new Cuboidf(
                    box.X1, 
                    box.Y1 - 1.0f, 
                    box.Z1, 
                    box.X2, 
                    box.Y2 - 1.0f, 
                    box.Z2
                );
                shiftedBoxes.Add(shiftedBox);
            }
            return shiftedBoxes.ToArray();
        }
        return base.GetSelectionBoxes(blockAccessor, pos);
    }
        

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        // Collision should ideally match selection so you don't walk through parts you can see
        return GetSelectionBoxes(blockAccessor, pos);
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
            if (be.OnInteract(byPlayer, blockSel)) return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
        
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [];
    }
}