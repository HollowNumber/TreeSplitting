using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

/// <summary>
/// The chopping block is a 1x2x1 multiblock (see the "Multiblock" behavior in its json): the game places a
/// vanilla proxy block above the stump and forwards everything aimed at it here through the IMultiBlock* interfaces,
/// with <c>offset</c> pointing from the proxy back to the stump.
/// </summary>
public class BlockChoppingBlock : Block, IMultiBlockColSelBoxes, IMultiBlockInteract, IMultiBlockBlockProperties
{
    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);

        // Worlds from before the multiblock switch may still have the old top block above an unconverted stump
        BlockPos upPos = pos.UpCopy();
        if (world.BlockAccessor.GetBlock(upPos) is BlockChoppingBlockTop)
        {
            world.BlockAccessor.SetBlock(0, upPos);
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BEChoppingBlock? be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEChoppingBlock;
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Delegate to BE
        if (be.OnInteract(byPlayer, blockSel)) return true;

        // The space above the stump belongs to the work item. Consume clicks on the top surface or the log so the
        // held block isn't placed there instead.
        return blockSel.Face == BlockFacing.UP || blockSel.SelectionBoxIndex > 0;
    }


    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        BEChoppingBlock? be = blockAccessor.GetBlockEntity(pos) as BEChoppingBlock;

        if (be is { SelectionBoxes.Length: > 0 })
        {
            Cuboidf[] combinedBoxes = (Cuboidf[])be.SelectionBoxes.Clone();
            Cuboidf[] stumpBoxes = base.GetSelectionBoxes(blockAccessor, pos);

            combinedBoxes[0] = stumpBoxes.Length > 0 ? stumpBoxes[0] : Cuboidf.Default();

            return combinedBoxes;
        }

        return base.GetSelectionBoxes(blockAccessor, pos);
    }


    // Highlight only the hovered voxel (like the anvil); otherwise the block highlight draws black
    // over every voxel box and hides the recipe outline
    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
    {
        return true;
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


    #region Multiblock proxy (the space above the stump)

    public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
    {
        return blockAccessor.GetBlockEntity(pos.AddCopy(offset)) is BEChoppingBlock be ? be.TopSelectionBoxes : [];
    }

    public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
    {
        return blockAccessor.GetBlockEntity(pos.AddCopy(offset)) is BEChoppingBlock be ? be.TopCollisionBoxes : [];
    }

    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset)
    {
        return true;
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position.AddCopy(offset)) is BEChoppingBlock be)
        {
            be.OnInteract(byPlayer, blockSel);
        }

        // Always consume the click so a held block isn't placed against the log
        return true;
    }

    public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
    {
        return false;
    }

    public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
    {
    }

    public bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset)
    {
        return true;
    }

    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset)
    {
        return OnPickBlock(world, pos.AddCopy(offset));
    }

    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, Vec3i offset)
    {
        BlockSelection stumpSel = blockSel.Clone();
        stumpSel.Position.Add(offset);
        return GetPlacedBlockInteractionHelp(world, stumpSel, forPlayer);
    }

    public BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack, Vec3i offset)
    {
        return Sounds;
    }

    // Nothing can hang off or rest on the empty space above the stump
    public bool MBCanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea, Vec3i offsetInv)
    {
        return false;
    }

    public float MBGetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, Vec3i offsetInv)
    {
        return 0;
    }

    public int MBGetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, Vec3i offsetInv)
    {
        return 0;
    }

    public JsonObject MBGetAttributes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return Attributes;
    }

    #endregion
}
