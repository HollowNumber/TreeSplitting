using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using TreeSplitting.BlockEntities;

namespace TreeSplitting.Blocks;

/// <summary>
/// 1x2x1 multiblock (the "Multiblock" behavior in the json). The game puts a vanilla block above the stump and
/// sends everything aimed at it through the MB methods down here, offset points from that block back to the stump.
/// </summary>
public class BlockChoppingBlock : Block, IMultiBlockColSelBoxes, IMultiBlockInteract, IMultiBlockBlockProperties
{
    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);

        // Old worlds might still have our old top block up there
        BlockPos upPos = pos.UpCopy();
        if (world.BlockAccessor.GetBlock(upPos) is BlockChoppingBlockTop)
        {
            world.BlockAccessor.SetBlock(0, upPos);
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (api is not ICoreClientAPI capi || world.BlockAccessor.GetBlockEntity(selection.Position) is not BEChoppingBlock be)
        {
            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        WorldInteraction[] interactions;
        if (be.WorkItemStack == null)
        {
            interactions = [new WorldInteraction
            {
                ActionLangCode = "treesplitting:blockhelp-choppingblock-place",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "shift",
                Itemstacks = GetLogStacks(capi)
            }];
        }
        else if (be.SelectedRecipe == null)
        {
            interactions =
            [
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-pickrecipe", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-turn", MouseButton = EnumMouseButton.Right, Itemstacks = TurningTools(capi) },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-take", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" }
            ];
        }
        else
        {
            interactions =
            [
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-chop", MouseButton = EnumMouseButton.Left, Itemstacks = ObjectCacheUtil.GetToolStacks(capi, EnumTool.Axe) },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-saw", MouseButton = EnumMouseButton.Left, Itemstacks = ObjectCacheUtil.GetToolStacks(capi, EnumTool.Saw) },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-carve", MouseButton = EnumMouseButton.Left, Itemstacks = ObjectCacheUtil.GetToolStacks(capi, EnumTool.Knife).Concat(ObjectCacheUtil.GetToolStacks(capi, EnumTool.Chisel)).ToArray() },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-toolmode", HotKeyCode = "toolmodeselect", MouseButton = EnumMouseButton.None },
                new WorldInteraction
                {
                    ActionLangCode = "treesplitting:blockhelp-choppingblock-turn",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = TurningTools(capi)
                },
                new WorldInteraction { ActionLangCode = "treesplitting:blockhelp-choppingblock-take", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" }
            ];
        }

        // Tipping the log over works until the first cut, or any time there is no recipe picked
        if (be.CanRotate)
        {
            interactions = interactions.Append(new WorldInteraction
            {
                ActionLangCode = be.Horizontal ? "treesplitting:blockhelp-choppingblock-standup" : "treesplitting:blockhelp-choppingblock-laydown",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "ctrl"
            });
        }

        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    private static ItemStack[] TurningTools(ICoreClientAPI capi) =>
        new[] { EnumTool.Axe, EnumTool.Saw, EnumTool.Knife, EnumTool.Chisel }.SelectMany(t => ObjectCacheUtil.GetToolStacks(capi, t)).ToArray();

    // Every log that has at least one hewing recipe, worked out once
    private static ItemStack[] GetLogStacks(ICoreClientAPI capi)
    {
        return ObjectCacheUtil.GetOrCreate(capi, "treesplittingLogStacks", () =>
        {
            var recipes = TreeSplittingModSystem.GetRecipes(capi);
            return capi.World.Blocks
                .Where(b => b.Code != null && recipes.Any(r => r.Matches(capi.World, new ItemStack(b))))
                .Select(b => new ItemStack(b))
                .ToArray();
        });
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BEChoppingBlock? be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEChoppingBlock;
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Delegate to BE
        if (be.OnInteract(byPlayer, blockSel)) return true;

        // Eat clicks on the top or the log so blocks can't be placed on it
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


    // Only highlight the voxel being looked at like the anvil, otherwise the black outline covers the orange one
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

        // Always eat the click so blocks can't be placed on the log
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

    // Nothing should attach to the space above the stump
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
